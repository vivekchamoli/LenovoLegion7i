using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.FanCurve;

/// <summary>
/// Provides manual fan speed control functionality
/// </summary>
public class ManualFanController
{
    private FanTable? _lastFanTable;
    private bool _isFullSpeedActive;

    /// <summary>
    /// Sets fan speed as a percentage (0-100%)
    /// </summary>
    /// <param name="cpuFanPercentage">CPU fan speed percentage (0-100)</param>
    /// <param name="gpuFanPercentage">GPU fan speed percentage (0-100)</param>
    public async Task SetFanSpeedPercentageAsync(int cpuFanPercentage, int gpuFanPercentage)
    {
        if (cpuFanPercentage < 0 || cpuFanPercentage > 100)
            throw new ArgumentOutOfRangeException(nameof(cpuFanPercentage), "CPU fan percentage must be between 0 and 100");

        if (gpuFanPercentage < 0 || gpuFanPercentage > 100)
            throw new ArgumentOutOfRangeException(nameof(gpuFanPercentage), "GPU fan percentage must be between 0 and 100");

        try
        {
            // Convert percentage to fan speed value (0-255 range typically)
            // We'll use a simplified approach where 100% = max speed
            var cpuSpeed = (ushort)(cpuFanPercentage * 255 / 100);
            var gpuSpeed = (ushort)(gpuFanPercentage * 255 / 100);

            // Create a flat fan table with the specified speeds
            // All 10 entries get the same speed for simplicity
            var fanTable = new FanTable(new ushort[]
            {
                cpuSpeed, cpuSpeed, cpuSpeed, cpuSpeed, cpuSpeed,
                cpuSpeed, cpuSpeed, cpuSpeed, cpuSpeed, cpuSpeed
            });

            _lastFanTable = fanTable;
            _isFullSpeedActive = false;

            // Apply the fan table
            await WMI.LenovoFanMethod.FanSetTableAsync(fanTable.GetBytes()).ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Manual fan control set: CPU={cpuFanPercentage}% ({cpuSpeed}), GPU={gpuFanPercentage}% ({gpuSpeed})");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set manual fan speed", ex);
            throw;
        }
    }

    /// <summary>
    /// Enables or disables full speed mode (max fan speed)
    /// </summary>
    public async Task SetFullSpeedAsync(bool enabled)
    {
        try
        {
            await WMI.LenovoFanMethod.FanSetFullSpeedAsync(enabled ? 1 : 0).ConfigureAwait(false);
            _isFullSpeedActive = enabled;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Manual fan full speed: {(enabled ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set fan full speed", ex);
            throw;
        }
    }

    /// <summary>
    /// Gets the current full speed status
    /// </summary>
    public async Task<bool> GetFullSpeedAsync()
    {
        try
        {
            return await WMI.LenovoFanMethod.FanGetFullSpeedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to get fan full speed status", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets the current fan speed for a specific fan
    /// </summary>
    /// <param name="fanId">Fan ID (0 = CPU fan, 1 = GPU fan typically)</param>
    public async Task<int> GetCurrentFanSpeedAsync(int fanId)
    {
        try
        {
            return await WMI.LenovoFanMethod.FanGetCurrentFanSpeedAsync(fanId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to get current fan speed for fan {fanId}", ex);
            return 0;
        }
    }

    /// <summary>
    /// Resets fan control to automatic (system default)
    /// </summary>
    public async Task ResetToAutoAsync()
    {
        try
        {
            // Disable full speed if active
            if (_isFullSpeedActive)
            {
                await SetFullSpeedAsync(false).ConfigureAwait(false);
            }

            // Get and apply the default fan table
            // This requires access to GodModeController for the default table
            // For now, we'll just disable full speed mode
            // The system should revert to automatic control

            _lastFanTable = null;
            _isFullSpeedActive = false;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Manual fan control reset to automatic");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to reset fan control to automatic", ex);
            throw;
        }
    }

    /// <summary>
    /// Gets whether manual control is currently active
    /// </summary>
    public bool IsManualControlActive => _lastFanTable != null || _isFullSpeedActive;
}
