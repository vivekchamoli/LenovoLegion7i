using System;
using System.Management;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Hardware Capability Detector
/// Detects what hardware control methods are available on this device
/// Prevents wasteful WMI calls that will always fail on unsupported hardware
/// </summary>
public class HardwareCapabilityDetector
{
    private static readonly object _lock = new();
    private static HardwareCapabilities? _cachedCapabilities = null;

    /// <summary>
    /// Get hardware capabilities (cached after first call)
    /// </summary>
    public static async Task<HardwareCapabilities> GetCapabilitiesAsync()
    {
        // Return cached if available
        lock (_lock)
        {
            if (_cachedCapabilities.HasValue)
                return _cachedCapabilities.Value;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Detecting hardware capabilities...");

        var capabilities = new HardwareCapabilities();

        // Test WMI CPU power control
        capabilities.WmiCpuPowerControl = await TestWmiCpuPowerControlAsync();

        // Test WMI fan control
        capabilities.WmiFanControl = await TestWmiFanControlAsync();

        // Cache the result
        lock (_lock)
        {
            _cachedCapabilities = capabilities;
        }

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Hardware capabilities detected:");
            Log.Instance.Trace($"  WMI CPU Power Control: {(capabilities.WmiCpuPowerControl ? "AVAILABLE" : "NOT SUPPORTED - will use MSR/HAL fallback")}");
            Log.Instance.Trace($"  WMI Fan Control: {(capabilities.WmiFanControl ? "AVAILABLE" : "NOT SUPPORTED - will use EC direct access")}");
        }

        return capabilities;
    }

    /// <summary>
    /// Test if WMI CPU power control is available
    /// </summary>
    private static async Task<bool> TestWmiCpuPowerControlAsync()
    {
        try
        {
            // Try to call CPU_Get_LongTerm_PowerLimit (read operation)
            // If this succeeds, WMI CPU control is supported
            var result = await LenovoLegionToolkit.Lib.System.Management.WMI.LenovoCpuMethod.CPUGetLongTermPowerLimitAsync();
            return true;
        }
        catch (ManagementException ex)
        {
            // "Not implemented" or "Generic failure" means WMI not supported
            if (ex.Message.Contains("not implemented", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("generic failure", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Other exceptions might be transient - assume supported
            return true;
        }
        catch
        {
            // Any other error - assume not supported
            return false;
        }
    }

    /// <summary>
    /// Test if WMI fan control is available
    /// </summary>
    private static async Task<bool> TestWmiFanControlAsync()
    {
        try
        {
            // Try to call Fan_Get_FullSpeed (read operation)
            // If this succeeds, WMI fan control is supported
            var result = await LenovoLegionToolkit.Lib.System.Management.WMI.LenovoFanMethod.FanGetFullSpeedAsync();
            return true;
        }
        catch (ManagementException ex)
        {
            // "Not implemented" or "Generic failure" means WMI not supported
            if (ex.Message.Contains("not implemented", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("generic failure", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Other exceptions might be transient - assume supported
            return true;
        }
        catch
        {
            // Any other error - assume not supported
            return false;
        }
    }

    /// <summary>
    /// Reset cached capabilities (for testing or after driver updates)
    /// </summary>
    public static void ResetCache()
    {
        lock (_lock)
        {
            _cachedCapabilities = null;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Hardware capabilities cache reset");
    }
}

/// <summary>
/// Hardware capabilities structure
/// </summary>
public struct HardwareCapabilities
{
    /// <summary>
    /// WMI CPU power control methods available (PL1/PL2/PL4)
    /// </summary>
    public bool WmiCpuPowerControl { get; set; }

    /// <summary>
    /// WMI fan control methods available (fan curves, profiles)
    /// </summary>
    public bool WmiFanControl { get; set; }
}
