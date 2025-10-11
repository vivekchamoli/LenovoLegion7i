using System;
using System.Runtime.InteropServices;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// Hybrid MSR Driver Manager - Simplified MSR Access
///
/// Tier 1: WinRing0x64.sys (proven MSR access driver)
/// Tier 2: Fallback (graceful degradation)
///
/// Benefits:
/// - Reliable MSR access (WinRing0 proven solution)
/// - Automatic fallback if driver unavailable
/// - No user configuration required
/// - Compatible with Test Signing mode
///
/// Power Savings: 5-15W through MSR-based CPU power control
/// </summary>
public class HybridMSRDriver
{
    private static HybridMSRDriver? _instance;
    private static readonly object _lock = new();

    public static HybridMSRDriver Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new HybridMSRDriver();
                }
            }
            return _instance;
        }
    }

    public enum DriverType
    {
        None = 0,
        WinRing0 = 1,         // WinRing0 driver (unsigned)
        Fallback = 2          // No MSR access
    }

    public enum DriverStatus
    {
        NotInitialized = 0,
        Initializing = 1,
        Available = 2,
        Unavailable = 3,
        Error = 4
    }

    private DriverType _activeDriver = DriverType.None;
    private DriverStatus _status = DriverStatus.NotInitialized;
    private string _statusMessage = "Not initialized";
    private bool _hasAttemptedInit = false;

    // WinRing0 driver P/Invoke
    [DllImport("WinRing0x64.dll", EntryPoint = "Rdmsr", SetLastError = true)]
    private static extern bool WinRing0_Rdmsr(uint index, out uint eax, out uint edx);

    [DllImport("WinRing0x64.dll", EntryPoint = "Wrmsr", SetLastError = true)]
    private static extern bool WinRing0_Wrmsr(uint index, uint eax, uint edx);

    [DllImport("WinRing0x64.dll", EntryPoint = "InitializeOls", SetLastError = true)]
    private static extern bool WinRing0_InitializeOls();

    [DllImport("WinRing0x64.dll", EntryPoint = "DeinitializeOls")]
    private static extern void WinRing0_DeinitializeOls();

    [DllImport("WinRing0x64.dll", EntryPoint = "GetDllStatus")]
    private static extern uint WinRing0_GetDllStatus();

    // Public properties
    public DriverType ActiveDriver => _activeDriver;
    public DriverStatus Status => _status;
    public string StatusMessage => _statusMessage;
    public bool IsAvailable => _status == DriverStatus.Available;
    public string DriverVersion => GetDriverVersion();

    /// <summary>
    /// Initialize hybrid driver system
    /// Tries WinRing0 → Fallback
    /// </summary>
    public bool Initialize()
    {
        if (_hasAttemptedInit && _status == DriverStatus.Available)
            return true;

        if (_hasAttemptedInit && _status == DriverStatus.Unavailable)
            return false;

        _hasAttemptedInit = true;
        _status = DriverStatus.Initializing;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[HybridMSRDriver] Initializing MSR driver system...");

        // Tier 1: Try WinRing0 (proven MSR access driver)
        if (TryInitializeWinRing0Driver())
        {
            _activeDriver = DriverType.WinRing0;
            _status = DriverStatus.Available;
            _statusMessage = "WinRing0 driver (v1.3.1.19)";

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[HybridMSRDriver] ✅ Tier 1 SUCCESS: WinRing0 driver initialized");

            return true;
        }

        // Tier 2: Fallback (no MSR access)
        _activeDriver = DriverType.Fallback;
        _status = DriverStatus.Unavailable;
        _statusMessage = "No MSR driver available - MSR access disabled";

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[HybridMSRDriver] ⚠️ Tier 2 FALLBACK: No MSR driver available");

        return false;
    }

    /// <summary>
    /// Try to initialize WinRing0 driver
    /// </summary>
    private bool TryInitializeWinRing0Driver()
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[HybridMSRDriver] Attempting WinRing0 driver initialization...");

            // Check if driver file exists
            var driverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinRing0x64.sys");
            if (!File.Exists(driverPath))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[HybridMSRDriver] WinRing0x64.sys not found at: {driverPath}");
                return false;
            }

            // Initialize WinRing0
            if (!WinRing0_InitializeOls())
            {
                var status = WinRing0_GetDllStatus();
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[HybridMSRDriver] WinRing0 initialization failed (Status: 0x{status:X})");
                return false;
            }

            // Test MSR read (MSR_PLATFORM_INFO = 0xCE, safe read-only register)
            if (!WinRing0_Rdmsr(0xCE, out uint _, out uint _))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[HybridMSRDriver] WinRing0 MSR test read failed");
                WinRing0_DeinitializeOls();
                return false;
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[HybridMSRDriver] WinRing0 driver initialized successfully");

            return true;
        }
        catch (DllNotFoundException)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[HybridMSRDriver] WinRing0x64.dll not found");
            return false;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[HybridMSRDriver] WinRing0 initialization failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Read MSR using active driver
    /// </summary>
    public bool ReadMSR(uint msr, out ulong value)
    {
        value = 0;

        if (!IsAvailable)
            return false;

        try
        {
            if (_activeDriver == DriverType.WinRing0)
                return ReadMSR_WinRing0(msr, out value);

            return false;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[HybridMSRDriver] MSR read failed: 0x{msr:X}", ex);
            return false;
        }
    }

    /// <summary>
    /// Write MSR using active driver
    /// </summary>
    public bool WriteMSR(uint msr, ulong value)
    {
        if (!IsAvailable)
            return false;

        try
        {
            if (_activeDriver == DriverType.WinRing0)
                return WriteMSR_WinRing0(msr, value);

            return false;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[HybridMSRDriver] MSR write failed: 0x{msr:X} = 0x{value:X}", ex);
            return false;
        }
    }

    /// <summary>
    /// Read MSR using WinRing0 driver
    /// </summary>
    private bool ReadMSR_WinRing0(uint msr, out ulong value)
    {
        value = 0;

        if (WinRing0_Rdmsr(msr, out uint eax, out uint edx))
        {
            value = ((ulong)edx << 32) | eax;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[WinRing0] MSR Read: 0x{msr:X} = 0x{value:X16}");

            return true;
        }

        return false;
    }

    /// <summary>
    /// Write MSR using WinRing0 driver
    /// </summary>
    private bool WriteMSR_WinRing0(uint msr, ulong value)
    {
        uint eax = (uint)(value & 0xFFFFFFFF);
        uint edx = (uint)((value >> 32) & 0xFFFFFFFF);

        if (WinRing0_Wrmsr(msr, eax, edx))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[WinRing0] MSR Write: 0x{msr:X} = 0x{value:X16}");

            return true;
        }

        return false;
    }

    /// <summary>
    /// Get driver version information
    /// </summary>
    private string GetDriverVersion()
    {
        switch (_activeDriver)
        {
            case DriverType.WinRing0:
                return "WinRing0 v1.3.1.19";

            case DriverType.Fallback:
                return "No driver (fallback mode)";

            default:
                return "Not initialized";
        }
    }

    /// <summary>
    /// Cleanup driver resources
    /// </summary>
    public void Cleanup()
    {
        try
        {
            if (_activeDriver == DriverType.WinRing0)
            {
                WinRing0_DeinitializeOls();

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[HybridMSRDriver] WinRing0 driver deinitialized");
            }

            _activeDriver = DriverType.None;
            _status = DriverStatus.NotInitialized;
            _hasAttemptedInit = false;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[HybridMSRDriver] Cleanup failed", ex);
        }
    }
}
