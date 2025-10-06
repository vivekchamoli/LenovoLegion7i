using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Memory Power Management - Memory power state control
/// Elite feature: Memory compression, standby optimization, page file control
/// </summary>
public class MemoryPowerManager
{
    private readonly object _lock = new();
    private bool _isAvailable;
    private MemoryPowerProfile _currentProfile = MemoryPowerProfile.Balanced;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetSystemFileCacheSize(IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize, int flags);

    private const int FILE_CACHE_MAX_HARD_ENABLE = 0x00000001;
    private const int FILE_CACHE_MIN_HARD_ENABLE = 0x00000004;

    public MemoryPowerManager()
    {
        _isAvailable = CheckAvailability();

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"MemoryPowerManager initialized: Available: {_isAvailable}");
        }
    }

    /// <summary>
    /// Check if memory power management is available
    /// </summary>
    private bool CheckAvailability()
    {
        try
        {
            // Check if we can access memory management registry
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Apply memory power profile
    /// </summary>
    public async Task<bool> ApplyMemoryProfileAsync(MemoryPowerProfile profile, string reason)
    {
        if (!_isAvailable)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Memory power management not available - graceful degradation");
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
            Log.Instance.Trace($"Applying memory power profile: {profile} ({reason})");
        }

        try
        {
            var settings = GetProfileSettings(profile);
            await Task.Run(() => ApplyMemorySettings(settings)).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply memory power profile", ex);
            return false;
        }
    }

    /// <summary>
    /// Get optimal memory profile based on system context
    /// </summary>
    public MemoryPowerProfile GetOptimalProfile(
        bool isOnBattery,
        int batteryPercent,
        long availableMemoryMB,
        long totalMemoryMB,
        bool isIdle)
    {
        var memoryUsagePercent = ((totalMemoryMB - availableMemoryMB) * 100) / totalMemoryMB;

        // Critical battery: Maximum compression and standby reduction
        if (isOnBattery && batteryPercent < 15)
            return MemoryPowerProfile.MaximumPowerSaving;

        // Low battery: Aggressive memory management
        if (isOnBattery && batteryPercent < 30)
            return MemoryPowerProfile.PowerSaving;

        // Idle + battery: Compress and reduce standby
        if (isOnBattery && isIdle)
            return MemoryPowerProfile.PowerSaving;

        // High memory usage: Prioritize performance
        if (memoryUsagePercent > 80)
            return MemoryPowerProfile.Performance;

        // Low memory: Compression to avoid paging
        if (availableMemoryMB < 2048) // < 2GB free
            return MemoryPowerProfile.Balanced;

        // AC power: Performance
        if (!isOnBattery)
            return MemoryPowerProfile.Performance;

        // Default: Balanced
        return MemoryPowerProfile.Balanced;
    }

    /// <summary>
    /// Get memory power settings for a profile
    /// </summary>
    private MemoryPowerSettings GetProfileSettings(MemoryPowerProfile profile)
    {
        return profile switch
        {
            MemoryPowerProfile.MaximumPowerSaving => new MemoryPowerSettings
            {
                CompressionEnabled = true,
                StandbyListPriority = StandbyListPriority.Low,      // Aggressively free standby
                WorkingSetTrim = WorkingSetTrimLevel.Aggressive,     // Trim inactive process memory
                SystemCacheSizeMB = 256,                             // Minimal system cache
                LargePageEnabled = false                             // Disable large pages (saves power)
            },
            MemoryPowerProfile.PowerSaving => new MemoryPowerSettings
            {
                CompressionEnabled = true,
                StandbyListPriority = StandbyListPriority.Normal,
                WorkingSetTrim = WorkingSetTrimLevel.Moderate,
                SystemCacheSizeMB = 512,
                LargePageEnabled = false
            },
            MemoryPowerProfile.Balanced => new MemoryPowerSettings
            {
                CompressionEnabled = true,
                StandbyListPriority = StandbyListPriority.Normal,
                WorkingSetTrim = WorkingSetTrimLevel.Normal,
                SystemCacheSizeMB = 1024,
                LargePageEnabled = true
            },
            MemoryPowerProfile.Performance => new MemoryPowerSettings
            {
                CompressionEnabled = false,                          // No compression overhead
                StandbyListPriority = StandbyListPriority.High,      // Keep standby list large
                WorkingSetTrim = WorkingSetTrimLevel.Minimal,        // Don't trim working sets
                SystemCacheSizeMB = 2048,                            // Large system cache
                LargePageEnabled = true
            },
            _ => GetProfileSettings(MemoryPowerProfile.Balanced)
        };
    }

    /// <summary>
    /// Apply memory power settings
    /// </summary>
    private void ApplyMemorySettings(MemoryPowerSettings settings)
    {
        try
        {
            // Apply memory compression
            ApplyMemoryCompression(settings.CompressionEnabled);

            // Apply standby list priority
            ApplyStandbyListPriority(settings.StandbyListPriority);

            // Apply working set trim
            ApplyWorkingSetTrim(settings.WorkingSetTrim);

            // Apply system cache size
            ApplySystemCacheSize(settings.SystemCacheSizeMB);

            // Apply large page support
            ApplyLargePageSupport(settings.LargePageEnabled);

            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"Applied memory settings: Compression={settings.CompressionEnabled}, StandbyPriority={settings.StandbyListPriority}, Cache={settings.SystemCacheSizeMB}MB");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply memory settings", ex);
        }
    }

    /// <summary>
    /// Enable/disable memory compression
    /// </summary>
    private void ApplyMemoryCompression(bool enabled)
    {
        try
        {
            var command = enabled
                ? "Enable-MMAgent -MemoryCompression"
                : "Disable-MMAgent -MemoryCompression";

            ExecutePowerShellCommand(command);
        }
        catch
        {
            // Graceful degradation
        }
    }

    /// <summary>
    /// Apply standby list priority
    /// </summary>
    private void ApplyStandbyListPriority(StandbyListPriority priority)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true);
            if (key == null) return;

            // StandbyListPriority: 0 = Low (aggressive free), 5 = Normal, 7 = High (keep cached)
            var priorityValue = priority switch
            {
                StandbyListPriority.Low => 0,
                StandbyListPriority.Normal => 5,
                StandbyListPriority.High => 7,
                _ => 5
            };

            key.SetValue("StandbyListPriority", priorityValue, RegistryValueKind.DWord);
        }
        catch
        {
            // Graceful degradation
        }
    }

    /// <summary>
    /// Apply working set trim level
    /// </summary>
    private void ApplyWorkingSetTrim(WorkingSetTrimLevel level)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true);
            if (key == null) return;

            // DisablePagingExecutive: 0 = Allow trim, 1 = Prevent trim
            var disableTrim = level switch
            {
                WorkingSetTrimLevel.Aggressive => 0,  // Allow aggressive trimming
                WorkingSetTrimLevel.Moderate => 0,
                WorkingSetTrimLevel.Normal => 0,
                WorkingSetTrimLevel.Minimal => 1,     // Prevent trimming (performance)
                _ => 0
            };

            key.SetValue("DisablePagingExecutive", disableTrim, RegistryValueKind.DWord);
        }
        catch
        {
            // Graceful degradation
        }
    }

    /// <summary>
    /// Apply system cache size limit
    /// </summary>
    private void ApplySystemCacheSize(int sizeMB)
    {
        try
        {
            var sizeBytes = (long)sizeMB * 1024 * 1024;
            var minSize = new IntPtr(sizeBytes / 2);  // Min = 50% of max
            var maxSize = new IntPtr(sizeBytes);

            SetSystemFileCacheSize(minSize, maxSize, FILE_CACHE_MAX_HARD_ENABLE | FILE_CACHE_MIN_HARD_ENABLE);
        }
        catch
        {
            // Graceful degradation
        }
    }

    /// <summary>
    /// Enable/disable large page support
    /// </summary>
    private void ApplyLargePageSupport(bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true);
            if (key == null) return;

            // LargeSystemCache: 0 = Disabled, 1 = Enabled
            key.SetValue("LargeSystemCache", enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch
        {
            // Graceful degradation
        }
    }

    /// <summary>
    /// Execute PowerShell command
    /// </summary>
    private void ExecutePowerShellCommand(string command)
    {
        try
        {
            var process = new global::System.Diagnostics.Process
            {
                StartInfo = new global::System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            process.WaitForExit(2000);
        }
        catch
        {
            // Graceful degradation
        }
    }

    /// <summary>
    /// Get current statistics
    /// </summary>
    public MemoryPowerStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new MemoryPowerStatistics
            {
                CurrentProfile = _currentProfile,
                IsAvailable = _isAvailable
            };
        }
    }
}

/// <summary>
/// Memory power profiles
/// </summary>
public enum MemoryPowerProfile
{
    MaximumPowerSaving,  // < 15% battery - aggressive compression/trim
    PowerSaving,         // < 30% battery - moderate management
    Balanced,            // Normal operation
    Performance          // AC power - maximize performance
}

/// <summary>
/// Memory power settings
/// </summary>
public class MemoryPowerSettings
{
    public bool CompressionEnabled { get; set; }
    public StandbyListPriority StandbyListPriority { get; set; }
    public WorkingSetTrimLevel WorkingSetTrim { get; set; }
    public int SystemCacheSizeMB { get; set; }
    public bool LargePageEnabled { get; set; }
}

/// <summary>
/// Standby list priority
/// </summary>
public enum StandbyListPriority
{
    Low,      // Aggressively free standby memory (power saving)
    Normal,   // Balanced
    High      // Keep large standby cache (performance)
}

/// <summary>
/// Working set trim level
/// </summary>
public enum WorkingSetTrimLevel
{
    Aggressive,  // Trim inactive process memory aggressively
    Moderate,    // Moderate trimming
    Normal,      // Standard Windows behavior
    Minimal      // Prevent trimming (performance)
}

/// <summary>
/// Memory power statistics
/// </summary>
public class MemoryPowerStatistics
{
    public MemoryPowerProfile CurrentProfile { get; set; }
    public bool IsAvailable { get; set; }
}
