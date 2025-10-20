using System;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// CRITICAL FIX: Global IOCTL throttler to prevent ACPI firmware race condition
///
/// ROOT CAUSE: Multiple concurrent battery-related IOCTLs trigger ACPI firmware bug:
/// - IOCTL_BATTERY_QUERY_STATUS (battery percentage)
/// - IOCTL_BATTERY_QUERY_INFORMATION (battery info)
/// - IOCTL_ENERGY_BATTERY_CHARGE_MODE (battery mode)
///
/// When >2 IOCTLs occur within 100ms, ACPI _BST object returns default value (100%)
/// instead of actual battery percentage, visible in Windows battery indicator.
///
/// SOLUTION: Global rate limiter ensures minimum 150ms gap between ANY battery IOCTLs
/// </summary>
public static class BatteryIOCTLThrottler
{
    private static readonly object _lock = new();
    private static DateTime _lastIOCTLTime = DateTime.MinValue;
    private static readonly TimeSpan MinimumIOCTLInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Wait if necessary to enforce minimum interval between battery IOCTLs
    /// Call this BEFORE any battery-related IOCTL operation
    /// </summary>
    public static async Task ThrottleAsync()
    {
        while (true)
        {
            TimeSpan waitTime;

            lock (_lock)
            {
                var timeSinceLastIOCTL = DateTime.UtcNow - _lastIOCTLTime;

                if (timeSinceLastIOCTL >= MinimumIOCTLInterval)
                {
                    // Enough time has passed - allow IOCTL
                    _lastIOCTLTime = DateTime.UtcNow;
                    return;
                }

                // Need to wait
                waitTime = MinimumIOCTLInterval - timeSinceLastIOCTL;
            }

            // Wait outside lock to allow other threads to check
            await Task.Delay(waitTime).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Synchronous version for non-async contexts
    /// </summary>
    public static void Throttle()
    {
        while (true)
        {
            TimeSpan waitTime;

            lock (_lock)
            {
                var timeSinceLastIOCTL = DateTime.UtcNow - _lastIOCTLTime;

                if (timeSinceLastIOCTL >= MinimumIOCTLInterval)
                {
                    // Enough time has passed - allow IOCTL
                    _lastIOCTLTime = DateTime.UtcNow;
                    return;
                }

                // Need to wait
                waitTime = MinimumIOCTLInterval - timeSinceLastIOCTL;
            }

            // Wait outside lock
            Thread.Sleep(waitTime);
        }
    }

    /// <summary>
    /// Get time since last IOCTL (for diagnostics)
    /// </summary>
    public static TimeSpan TimeSinceLastIOCTL()
    {
        lock (_lock)
        {
            return DateTime.UtcNow - _lastIOCTLTime;
        }
    }
}
