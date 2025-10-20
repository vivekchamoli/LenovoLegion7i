using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Testing;

/// <summary>
/// Battery percentage calculation validation and testing
/// Validates the BATTERY_CAPACITY_RELATIVE fix is working correctly
/// </summary>
public static class BatteryPercentageValidation
{
    /// <summary>
    /// Run comprehensive battery percentage validation
    /// Compares IOCTL method against WMI method
    /// </summary>
    public static void ValidateBatteryPercentage()
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"=== Battery Percentage Validation ===");

            // Get battery info using IOCTL (with BATTERY_CAPACITY_RELATIVE fix)
            var batteryInfo = Battery.GetBatteryInformation();
            var ioctlPercentage = batteryInfo.BatteryPercentage;

            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"IOCTL Method: {ioctlPercentage}%");
                Log.Instance.Trace($"  - Remaining Capacity: {batteryInfo.EstimateChargeRemaining}mWh");
                Log.Instance.Trace($"  - Full Charge Capacity: {batteryInfo.FullChargeCapacity}mWh");
                Log.Instance.Trace($"  - Design Capacity: {batteryInfo.DesignCapacity}mWh");
                Log.Instance.Trace($"  - Battery Health: {batteryInfo.BatteryHealth:F2}%");
            }

            // Get battery info using WMI (more reliable baseline)
            var wmiPercentage = BatteryWmi.GetBatteryPercentageFromWmi();
            if (wmiPercentage.HasValue)
            {
                var difference = Math.Abs(ioctlPercentage - wmiPercentage.Value);

                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"WMI Method: {wmiPercentage}%");
                    Log.Instance.Trace($"Difference: {difference}% ({(difference <= 5 ? "PASS" : "FAIL")})");
                }

                // Get detailed WMI capacities for debugging
                var wmiCapacities = BatteryWmi.GetBatteryCapacitiesFromWmi();
                if (wmiCapacities.HasValue)
                {
                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"WMI Capacities:");
                        Log.Instance.Trace($"  - Remaining: {wmiCapacities.Value.RemainingCapacity}mWh");
                        Log.Instance.Trace($"  - Full Charged: {wmiCapacities.Value.FullChargedCapacity}mWh");
                        Log.Instance.Trace($"  - Design: {wmiCapacities.Value.DesignCapacity}mWh");
                    }
                }

                // Validation result
                if (difference > 5)
                {
                    if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"WARNING: Battery percentage difference exceeds 5% threshold");
                        Log.Instance.Trace($"This may indicate a BATTERY_CAPACITY_RELATIVE issue or WMI/IOCTL discrepancy");
                    }
                }
                else
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Battery percentage validation PASSED");
                }
            }
            else
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"WMI method unavailable - cannot validate");
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"=== Validation Complete ===");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery percentage validation failed", ex);
        }
    }

    /// <summary>
    /// Test battery percentage calculation over time
    /// Run this for 30 seconds to observe fluctuation patterns
    /// </summary>
    public static async Task TestBatteryFluctuation(int durationSeconds = 30)
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"=== Battery Fluctuation Test ({durationSeconds}s) ===");

            int? previousPercentage = null;
            int fluctuationCount = 0;
            int maxFluctuation = 0;

            var endTime = DateTime.Now.AddSeconds(durationSeconds);

            while (DateTime.Now < endTime)
            {
                var batteryInfo = Battery.GetBatteryInformation();
                var currentPercentage = batteryInfo.BatteryPercentage;

                if (previousPercentage.HasValue)
                {
                    var change = Math.Abs(currentPercentage - previousPercentage.Value);
                    if (change > 0)
                    {
                        fluctuationCount++;
                        maxFluctuation = Math.Max(maxFluctuation, change);

                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Fluctuation detected: {previousPercentage}% -> {currentPercentage}% (Δ{change}%)");
                    }
                }

                previousPercentage = currentPercentage;

                await Task.Delay(1000);
            }

            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"=== Test Results ===");
                Log.Instance.Trace($"Total fluctuations: {fluctuationCount}");
                Log.Instance.Trace($"Max fluctuation: {maxFluctuation}%");
                Log.Instance.Trace($"Status: {(maxFluctuation > 1 ? "UNSTABLE" : "STABLE")}");
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery fluctuation test failed", ex);
        }
    }
}
