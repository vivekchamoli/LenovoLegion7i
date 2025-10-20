using System;
using System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// WMI-based battery information provider (root\wmi namespace)
/// More reliable than GetSystemPowerStatus() for percentage, useful as validation/fallback
/// </summary>
public static class BatteryWmi
{
    /// <summary>
    /// Get battery percentage using WMI root\wmi namespace
    /// This is often more accurate than IOCTL on some systems
    /// Returns null if WMI query fails
    /// </summary>
    public static int? GetBatteryPercentageFromWmi()
    {
        try
        {
            // Query BatteryStatus for current charge
            uint? currentCharge = null;
            using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT RemainingCapacity FROM BatteryStatus"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    currentCharge = (uint?)obj["RemainingCapacity"];
                    break; // Get first battery
                }
            }

            // Query BatteryFullChargedCapacity for max charge
            uint? fullCharge = null;
            using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    fullCharge = (uint?)obj["FullChargedCapacity"];
                    break; // Get first battery
                }
            }

            if (currentCharge.HasValue && fullCharge.HasValue && fullCharge.Value > 0)
            {
                var percentage = (int)Math.Round((double)currentCharge.Value / fullCharge.Value * 100.0, 0, MidpointRounding.AwayFromZero);
                percentage = Math.Max(0, Math.Min(100, percentage));

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"WMI battery: {currentCharge}mWh / {fullCharge}mWh = {percentage}%");

                return percentage;
            }

            return null;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to get battery percentage from WMI", ex);
            return null;
        }
    }

    /// <summary>
    /// Get full battery information from WMI root\wmi namespace
    /// Useful for validation or when IOCTL is unreliable
    /// </summary>
    public static (uint? DesignCapacity, uint? FullChargedCapacity, uint? RemainingCapacity)? GetBatteryCapacitiesFromWmi()
    {
        try
        {
            uint? designCapacity = null;
            uint? fullChargedCapacity = null;
            uint? remainingCapacity = null;

            // Get design capacity from BatteryStaticData
            using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT DesignedCapacity FROM BatteryStaticData"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    designCapacity = (uint?)obj["DesignedCapacity"];
                    break;
                }
            }

            // Get full charged capacity
            using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    fullChargedCapacity = (uint?)obj["FullChargedCapacity"];
                    break;
                }
            }

            // Get remaining capacity from BatteryStatus
            using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT RemainingCapacity FROM BatteryStatus"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    remainingCapacity = (uint?)obj["RemainingCapacity"];
                    break;
                }
            }

            if (designCapacity.HasValue || fullChargedCapacity.HasValue || remainingCapacity.HasValue)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"WMI battery capacities: Design={designCapacity}mWh, FullCharged={fullChargedCapacity}mWh, Remaining={remainingCapacity}mWh");

                return (designCapacity, fullChargedCapacity, remainingCapacity);
            }

            return null;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to get battery capacities from WMI", ex);
            return null;
        }
    }

    /// <summary>
    /// Validate IOCTL battery percentage against WMI
    /// Returns true if values are within acceptable range (±5%)
    /// Useful for detecting BATTERY_CAPACITY_RELATIVE issues
    /// </summary>
    public static bool ValidateBatteryPercentage(int ioctlPercentage)
    {
        try
        {
            var wmiPercentage = GetBatteryPercentageFromWmi();
            if (!wmiPercentage.HasValue)
                return true; // Can't validate, assume IOCTL is correct

            var difference = Math.Abs(ioctlPercentage - wmiPercentage.Value);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery validation: IOCTL={ioctlPercentage}%, WMI={wmiPercentage}%, Diff={difference}%");

            // Allow up to 5% difference (batteries report slightly different values)
            return difference <= 5;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to validate battery percentage", ex);
            return true; // Validation failed, assume IOCTL is correct
        }
    }
}
