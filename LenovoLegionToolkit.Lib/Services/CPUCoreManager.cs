using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// CPU Per-Core Management - Fine-grained CPU power control
/// Elite feature: Per-core frequency control and core parking
/// </summary>
public class CPUCoreManager
{
    private readonly object _lock = new();
    private int _coreCount;
    private bool _isAvailable;
    private CoreParkingProfile _currentProfile = CoreParkingProfile.Balanced;

    public CPUCoreManager()
    {
        _coreCount = Environment.ProcessorCount;
        _isAvailable = CheckAvailability();

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"CPUCoreManager initialized: {_coreCount} cores, Available: {_isAvailable}");
        }
    }

    /// <summary>
    /// Check if per-core control is available
    /// </summary>
    private bool CheckAvailability()
    {
        try
        {
            // Check if we can access power settings registry
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583");
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Apply core parking profile based on workload
    /// </summary>
    public async Task<bool> ApplyCoreParkingProfileAsync(CoreParkingProfile profile, string reason)
    {
        if (!_isAvailable)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Core parking not available - graceful degradation");
            return false;
        }

        lock (_lock)
        {
            if (_currentProfile == profile)
                return false; // No change needed

            _currentProfile = profile;
        }

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Applying core parking profile: {profile} ({reason})");
        }

        try
        {
            var settings = GetProfileSettings(profile);
            await Task.Run(() => ApplyCoreParkingSettings(settings)).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply core parking profile", ex);
            return false;
        }
    }

    /// <summary>
    /// Get optimal core parking profile based on system context
    /// </summary>
    public CoreParkingProfile GetOptimalProfile(
        bool isOnBattery,
        double cpuUsage,
        int batteryPercent,
        bool isThermalHigh)
    {
        // Critical battery: Aggressive parking (use minimal cores)
        if (isOnBattery && batteryPercent < 15)
            return CoreParkingProfile.MaximumPowerSaving;

        // Low battery: Park half the cores
        if (isOnBattery && batteryPercent < 30)
            return CoreParkingProfile.PowerSaving;

        // High thermal + low CPU usage: Park cores to reduce heat
        if (isThermalHigh && cpuUsage < 30)
            return CoreParkingProfile.PowerSaving;

        // High CPU usage: All cores available
        if (cpuUsage > 60)
            return CoreParkingProfile.Performance;

        // AC power + high workload: Performance
        if (!isOnBattery && cpuUsage > 40)
            return CoreParkingProfile.Performance;

        // Default: Balanced
        return CoreParkingProfile.Balanced;
    }

    /// <summary>
    /// Get core parking settings for a profile
    /// </summary>
    private CoreParkingSettings GetProfileSettings(CoreParkingProfile profile)
    {
        return profile switch
        {
            CoreParkingProfile.MaximumPowerSaving => new CoreParkingSettings
            {
                MinCores = 25,           // Keep only 25% cores active
                MaxCores = 50,           // Cap at 50% max
                IncreaseThreshold = 80,  // Park aggressively
                DecreaseThreshold = 20,
                IncreaseTime = 3,        // Slow to unpark (3 checks)
                DecreaseTime = 1         // Quick to park
            },
            CoreParkingProfile.PowerSaving => new CoreParkingSettings
            {
                MinCores = 50,           // Keep 50% cores active
                MaxCores = 75,           // Cap at 75%
                IncreaseThreshold = 70,
                DecreaseThreshold = 30,
                IncreaseTime = 2,
                DecreaseTime = 1
            },
            CoreParkingProfile.Balanced => new CoreParkingSettings
            {
                MinCores = 50,           // 50% minimum
                MaxCores = 100,          // All cores available
                IncreaseThreshold = 60,
                DecreaseThreshold = 40,
                IncreaseTime = 1,
                DecreaseTime = 1
            },
            CoreParkingProfile.Performance => new CoreParkingSettings
            {
                MinCores = 100,          // All cores always active
                MaxCores = 100,
                IncreaseThreshold = 50,  // Unpark quickly
                DecreaseThreshold = 40,
                IncreaseTime = 1,
                DecreaseTime = 2         // Slow to park
            },
            _ => GetProfileSettings(CoreParkingProfile.Balanced)
        };
    }

    /// <summary>
    /// Apply core parking settings via registry
    /// </summary>
    private void ApplyCoreParkingSettings(CoreParkingSettings settings)
    {
        try
        {
            // Core parking settings GUID
            const string coreParkingGuid = "54533251-82be-4824-96c1-47b60b740d00";

            // Apply for both AC and DC (battery)
            ApplyPowerSetting(coreParkingGuid, "0cc5b647-c1df-4637-891a-dec35c318583", settings.MinCores, "AC");   // Min cores
            ApplyPowerSetting(coreParkingGuid, "0cc5b647-c1df-4637-891a-dec35c318583", settings.MinCores, "DC");
            ApplyPowerSetting(coreParkingGuid, "ea062031-0e34-4ff1-9b6d-eb1059334028", settings.MaxCores, "AC");   // Max cores
            ApplyPowerSetting(coreParkingGuid, "ea062031-0e34-4ff1-9b6d-eb1059334028", settings.MaxCores, "DC");
            ApplyPowerSetting(coreParkingGuid, "2ddd5a84-5a71-437e-912a-db0b8c788732", settings.IncreaseThreshold, "AC"); // Increase threshold
            ApplyPowerSetting(coreParkingGuid, "2ddd5a84-5a71-437e-912a-db0b8c788732", settings.IncreaseThreshold, "DC");
            ApplyPowerSetting(coreParkingGuid, "68dd2f27-a4ce-4e11-8487-3794e4135dfa", settings.DecreaseThreshold, "AC"); // Decrease threshold
            ApplyPowerSetting(coreParkingGuid, "68dd2f27-a4ce-4e11-8487-3794e4135dfa", settings.DecreaseThreshold, "DC");

            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"Applied core parking: Min={settings.MinCores}%, Max={settings.MaxCores}%, IncThreshold={settings.IncreaseThreshold}%");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply core parking settings", ex);
        }
    }

    /// <summary>
    /// Apply a single power setting via powercfg
    /// </summary>
    private void ApplyPowerSetting(string subgroup, string setting, int value, string mode)
    {
        try
        {
            var powerScheme = GetActivePowerScheme();
            var hexValue = value.ToString("X8");

            var command = mode == "AC"
                ? $"powercfg /setacvalueindex {powerScheme} {subgroup} {setting} {hexValue}"
                : $"powercfg /setdcvalueindex {powerScheme} {subgroup} {setting} {hexValue}";

            var process = global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            process?.WaitForExit(1000);
        }
        catch
        {
            // Graceful degradation - log already handled in caller
        }
    }

    /// <summary>
    /// Get active power scheme GUID
    /// </summary>
    private string GetActivePowerScheme()
    {
        try
        {
            var process = new global::System.Diagnostics.Process
            {
                StartInfo = new global::System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Extract GUID from output: "Power Scheme GUID: {GUID} (Name)"
            var guidStart = output.IndexOf('{');
            var guidEnd = output.IndexOf('}');
            if (guidStart >= 0 && guidEnd > guidStart)
            {
                return output.Substring(guidStart, guidEnd - guidStart + 1);
            }
        }
        catch
        {
            // Fall back to balanced scheme GUID
        }

        return "381b4222-f694-41f0-9685-ff5bb260df2e"; // Balanced scheme GUID
    }

    /// <summary>
    /// Get current statistics
    /// </summary>
    public CoreParkingStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new CoreParkingStatistics
            {
                TotalCores = _coreCount,
                CurrentProfile = _currentProfile,
                IsAvailable = _isAvailable
            };
        }
    }
}

/// <summary>
/// Core parking profiles
/// </summary>
public enum CoreParkingProfile
{
    MaximumPowerSaving,  // < 15% battery - minimal cores
    PowerSaving,         // < 30% battery or thermal - reduced cores
    Balanced,            // Normal operation
    Performance          // High workload - all cores active
}

/// <summary>
/// Core parking settings
/// </summary>
public class CoreParkingSettings
{
    public int MinCores { get; set; }           // Minimum cores unparked (%)
    public int MaxCores { get; set; }           // Maximum cores unparked (%)
    public int IncreaseThreshold { get; set; }  // CPU% to unpark cores
    public int DecreaseThreshold { get; set; }  // CPU% to park cores
    public int IncreaseTime { get; set; }       // Checks before unpark
    public int DecreaseTime { get; set; }       // Checks before park
}

/// <summary>
/// Core parking statistics
/// </summary>
public class CoreParkingStatistics
{
    public int TotalCores { get; set; }
    public CoreParkingProfile CurrentProfile { get; set; }
    public bool IsAvailable { get; set; }
}
