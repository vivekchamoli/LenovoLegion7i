using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// NVIDIA System Management Interface (nvidia-smi) Wrapper
/// Provides complete GPU power management using NVIDIA's official CLI tool
///
/// ADVANTAGES over NvAPIWrapper:
/// - Official NVIDIA tool (always up-to-date)
/// - Complete feature coverage (power limits, P-states, monitoring)
/// - No library dependencies
/// - Works with all NVIDIA GPUs
///
/// REQUIREMENTS:
/// - NVIDIA GPU with driver installed
/// - nvidia-smi.exe in PATH or driver folder
///
/// CAPABILITIES:
/// - Power limit control (TGP)
/// - Power consumption monitoring
/// - P-state monitoring
/// - Clock speed monitoring
/// - Temperature monitoring
/// - Application clock control
/// - Persistence mode control
/// </summary>
public class NvidiaSMI
{
    private string? _nvidiaSmiPath;
    private bool _isAvailable = false;
    private bool _hasCheckedAvailability = false;

    /// <summary>
    /// Check if nvidia-smi is available
    /// </summary>
    public bool IsAvailable()
    {
        if (_hasCheckedAvailability)
            return _isAvailable;

        _hasCheckedAvailability = true;

        try
        {
            // Try to find nvidia-smi.exe
            _nvidiaSmiPath = FindNvidiaSmi();

            if (_nvidiaSmiPath == null)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"nvidia-smi not found");
                _isAvailable = false;
                return false;
            }

            // Test if nvidia-smi works
            var output = RunNvidiaSmi("--query-gpu=name --format=csv,noheader");
            if (string.IsNullOrEmpty(output))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"nvidia-smi found but not responding");
                _isAvailable = false;
                return false;
            }

            _isAvailable = true;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"nvidia-smi available - complete GPU control enabled (GPU: {output.Trim()})");

            return true;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"nvidia-smi availability check failed", ex);

            _isAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Find nvidia-smi.exe location
    /// </summary>
    private string? FindNvidiaSmi()
    {
        // Check common locations
        var possiblePaths = new[]
        {
            @"C:\Windows\System32\nvidia-smi.exe",
            @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Try PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';'))
        {
            try
            {
                var nvidiaSmiPath = Path.Combine(dir.Trim(), "nvidia-smi.exe");
                if (File.Exists(nvidiaSmiPath))
                    return nvidiaSmiPath;
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Run nvidia-smi command and return output
    /// </summary>
    private string RunNvidiaSmi(string arguments)
    {
        if (_nvidiaSmiPath == null || !_isAvailable)
            return "";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _nvidiaSmiPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return "";

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            if (!string.IsNullOrWhiteSpace(error))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"nvidia-smi error: {error}");
            }

            return output;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to run nvidia-smi: {arguments}", ex);
            return "";
        }
    }

    /// <summary>
    /// Set GPU power limit (TGP) in watts
    /// Example: SetPowerLimit(100) sets 100W limit
    /// </summary>
    public bool SetPowerLimit(int watts)
    {
        if (!IsAvailable())
            return false;

        try
        {
            // nvidia-smi -pl <watts>
            var output = RunNvidiaSmi($"-pl {watts}");

            var success = output.Contains("power limit has been set", StringComparison.OrdinalIgnoreCase);

            if (success && Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU power limit set to {watts}W");
            else if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set GPU power limit: {output}");

            return success;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set power limit", ex);
            return false;
        }
    }

    /// <summary>
    /// Get current GPU power consumption in watts
    /// </summary>
    public double GetPowerDrawWatts()
    {
        if (!IsAvailable())
            return 0;

        try
        {
            // nvidia-smi --query-gpu=power.draw --format=csv,noheader,nounits
            var output = RunNvidiaSmi("--query-gpu=power.draw --format=csv,noheader,nounits");

            if (double.TryParse(output.Trim(), out var watts))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"GPU power draw: {watts}W");
                return watts;
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to get power draw", ex);
            return 0;
        }
    }

    /// <summary>
    /// Get GPU power limits (default, min, max, current)
    /// </summary>
    public GPUPowerLimits GetPowerLimits()
    {
        if (!IsAvailable())
            return new GPUPowerLimits();

        try
        {
            // Query multiple power limit values
            var output = RunNvidiaSmi("--query-gpu=power.limit,power.default_limit,power.min_limit,power.max_limit --format=csv,noheader,nounits");

            var values = output.Trim().Split(',').Select(v => v.Trim()).ToArray();

            if (values.Length >= 4 &&
                double.TryParse(values[0], out var current) &&
                double.TryParse(values[1], out var defaultLimit) &&
                double.TryParse(values[2], out var min) &&
                double.TryParse(values[3], out var max))
            {
                return new GPUPowerLimits
                {
                    CurrentWatts = (int)current,
                    DefaultWatts = (int)defaultLimit,
                    MinWatts = (int)min,
                    MaxWatts = (int)max
                };
            }

            return new GPUPowerLimits();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to get power limits", ex);
            return new GPUPowerLimits();
        }
    }

    /// <summary>
    /// Get comprehensive GPU status
    /// </summary>
    public NvidiaSMIGPUStatus GetGPUStatus()
    {
        if (!IsAvailable())
            return new NvidiaSMIGPUStatus();

        try
        {
            // Query all relevant GPU metrics in one call
            var query = "--query-gpu=name,temperature.gpu,utilization.gpu,utilization.memory," +
                       "clocks.current.graphics,clocks.current.memory,clocks.max.graphics,clocks.max.memory," +
                       "power.draw,pstate,fan.speed --format=csv,noheader,nounits";

            var output = RunNvidiaSmi(query);
            var values = output.Trim().Split(',').Select(v => v.Trim()).ToArray();

            if (values.Length >= 11)
            {
                return new NvidiaSMIGPUStatus
                {
                    Name = values[0],
                    TemperatureC = int.TryParse(values[1], out var temp) ? temp : 0,
                    UtilizationPercent = int.TryParse(values[2], out var gpuUtil) ? gpuUtil : 0,
                    MemoryUtilizationPercent = int.TryParse(values[3], out var memUtil) ? memUtil : 0,
                    CoreClockMHz = int.TryParse(values[4], out var coreClock) ? coreClock : 0,
                    MemoryClockMHz = int.TryParse(values[5], out var memClock) ? memClock : 0,
                    MaxCoreClockMHz = int.TryParse(values[6], out var maxCore) ? maxCore : 0,
                    MaxMemoryClockMHz = int.TryParse(values[7], out var maxMem) ? maxMem : 0,
                    PowerDrawWatts = double.TryParse(values[8], out var power) ? power : 0,
                    PState = values[9],
                    FanSpeedPercent = int.TryParse(values[10], out var fan) ? fan : 0
                };
            }

            return new NvidiaSMIGPUStatus();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to get GPU status", ex);
            return new NvidiaSMIGPUStatus();
        }
    }

    /// <summary>
    /// Set application clocks (locks clocks for consistent performance)
    /// Example: SetApplicationClocks(2100, 7000) sets 2100MHz core, 7000MHz memory
    /// </summary>
    public bool SetApplicationClocks(int graphicsMHz, int memoryMHz)
    {
        if (!IsAvailable())
            return false;

        try
        {
            // nvidia-smi -ac <memory,graphics>
            var output = RunNvidiaSmi($"-ac {memoryMHz},{graphicsMHz}");

            var success = output.Contains("applications clocks set", StringComparison.OrdinalIgnoreCase) ||
                         output.Contains("successfully", StringComparison.OrdinalIgnoreCase);

            if (success && Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Application clocks set: Core={graphicsMHz}MHz, Memory={memoryMHz}MHz");
            else if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set application clocks: {output}");

            return success;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set application clocks", ex);
            return false;
        }
    }

    /// <summary>
    /// Reset application clocks to default (allow GPU boost)
    /// </summary>
    public bool ResetApplicationClocks()
    {
        if (!IsAvailable())
            return false;

        try
        {
            // nvidia-smi -rac
            var output = RunNvidiaSmi("-rac");

            var success = output.Contains("reset", StringComparison.OrdinalIgnoreCase) ||
                         output.Contains("successfully", StringComparison.OrdinalIgnoreCase);

            if (success && Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Application clocks reset to default");

            return success;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to reset application clocks", ex);
            return false;
        }
    }

    /// <summary>
    /// Enable persistence mode (keeps driver loaded, reduces latency)
    /// Recommended for gaming/workstation use
    /// </summary>
    public bool SetPersistenceMode(bool enabled)
    {
        if (!IsAvailable())
            return false;

        try
        {
            // nvidia-smi -pm <0|1>
            var mode = enabled ? "1" : "0";
            var output = RunNvidiaSmi($"-pm {mode}");

            var success = output.Contains("persistence mode", StringComparison.OrdinalIgnoreCase);

            if (success && Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Persistence mode {(enabled ? "enabled" : "disabled")}");

            return success;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set persistence mode", ex);
            return false;
        }
    }

    /// <summary>
    /// Get GPU name/model
    /// </summary>
    public string GetGPUName()
    {
        if (!IsAvailable())
            return "";

        try
        {
            var output = RunNvidiaSmi("--query-gpu=name --format=csv,noheader");
            return output.Trim();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Apply optimized profile for specific workload
    /// </summary>
    public void ApplyMediaPlaybackProfile()
    {
        if (!IsAvailable())
            return;

        try
        {
            var limits = GetPowerLimits();

            // Set minimum power limit (10W or min available)
            var minPower = Math.Max(10, limits.MinWatts);
            SetPowerLimit(minPower);

            // Lock clocks to idle speeds
            // Note: Some GPUs don't support low application clocks, will fail gracefully
            SetApplicationClocks(300, 405);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Applied media playback profile: {minPower}W, 300MHz core, 405MHz memory");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply media playback profile", ex);
        }
    }

    /// <summary>
    /// Apply gaming profile (maximum performance)
    /// </summary>
    public void ApplyGamingProfile()
    {
        if (!IsAvailable())
            return;

        try
        {
            var limits = GetPowerLimits();

            // Set maximum power limit
            SetPowerLimit(limits.MaxWatts);

            // Reset clocks to allow boost
            ResetApplicationClocks();

            // Enable persistence mode for lower latency
            SetPersistenceMode(true);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Applied gaming profile: {limits.MaxWatts}W, clocks unlocked, persistence enabled");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply gaming profile", ex);
        }
    }

    /// <summary>
    /// Apply balanced profile
    /// </summary>
    public void ApplyBalancedProfile()
    {
        if (!IsAvailable())
            return;

        try
        {
            var limits = GetPowerLimits();

            // Set 85% of maximum power
            var balancedPower = (int)(limits.MaxWatts * 0.85);
            SetPowerLimit(balancedPower);

            // Reset clocks to default behavior
            ResetApplicationClocks();

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Applied balanced profile: {balancedPower}W ({limits.MaxWatts * 0.85:F0}% of max)");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply balanced profile", ex);
        }
    }
}

/// <summary>
/// GPU power limit information
/// </summary>
public class GPUPowerLimits
{
    public int CurrentWatts { get; set; }
    public int DefaultWatts { get; set; }
    public int MinWatts { get; set; }
    public int MaxWatts { get; set; }
}

/// <summary>
/// Comprehensive GPU status from nvidia-smi
/// </summary>
public class NvidiaSMIGPUStatus
{
    public string Name { get; set; } = "";
    public int TemperatureC { get; set; }
    public int UtilizationPercent { get; set; }
    public int MemoryUtilizationPercent { get; set; }
    public int CoreClockMHz { get; set; }
    public int MemoryClockMHz { get; set; }
    public int MaxCoreClockMHz { get; set; }
    public int MaxMemoryClockMHz { get; set; }
    public double PowerDrawWatts { get; set; }
    public string PState { get; set; } = "P0";
    public int FanSpeedPercent { get; set; }
}
