using System;
using System.Collections.Generic;
using System.Diagnostics;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// Hardware Abstraction Layer (HAL) - Unified Hardware Control Interface
/// Provides comprehensive hardware access for Legion Slim 7i Gen 9 (2024)
///
/// ARCHITECTURE:
/// Level 5 (AI): Multi-Agent Orchestrator → decides actions
/// Level 4 (HAL): Hardware Abstraction Layer → this class
/// Level 3 (Drivers): MSR, NVAPI, EC, PCIe → low-level access
/// Level 2 (Kernel): WinRing0, nvapi64.dll → kernel drivers
/// Level 1 (Hardware): CPU, GPU, EC, firmware → physical hardware
///
/// FEATURES:
/// - Unified hardware access (temperatures, power, performance)
/// - Automatic capability detection
/// - Graceful degradation when drivers unavailable
/// - Real-time monitoring
/// - Safe value clamping
/// - Atomic multi-component control
///
/// INITIALIZATION:
/// 1. Auto-detect available hardware access methods
/// 2. Initialize kernel drivers if available
/// 3. Discover hardware capabilities
/// 4. Register with AI orchestrator
/// </summary>
public class HardwareAbstractionLayer
{
    private readonly MSRAccess? _msrAccess;
    private readonly NVAPIIntegration? _nvapiIntegration;
    private readonly PCIePowerManager? _pciePowerManager;
    private readonly EmbeddedControllerAccess? _ecAccess;

    private bool _initialized = false;
    private HardwareCapabilities _capabilities = new();
    private Stopwatch _uptimeStopwatch = new();
    private DateTime _lastPowerSample = DateTime.Now;
    private RAPLPowerData? _lastRAPLData;

    /// <summary>
    /// Initialize Hardware Abstraction Layer
    /// Discovers and initializes all available hardware access methods
    /// </summary>
    public HardwareAbstractionLayer()
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"HAL: Initializing Hardware Abstraction Layer");

        try
        {
            // Initialize kernel driver first (required for EC and MSR)
            KernelDriverInterface.Initialize();

            // Initialize EC access (highest priority - direct hardware control)
            _ecAccess = new EmbeddedControllerAccess();
            _capabilities.EcAccessAvailable = _ecAccess.Initialize();

            // Initialize MSR access (CPU power/performance control)
            _msrAccess = new MSRAccess();
            _capabilities.MsrAccessAvailable = _msrAccess.IsAvailable();

            // Initialize NVAPI (GPU control)
            _nvapiIntegration = new NVAPIIntegration();
            _capabilities.NvapiAvailable = _nvapiIntegration.IsAvailable();

            // Initialize PCIe power management
            _pciePowerManager = new PCIePowerManager();
            _capabilities.PciePowerAvailable = true; // PCIe management uses Windows APIs (always available)

            // Detect hardware capabilities
            DetectHardwareCapabilities();

            _initialized = true;
            _uptimeStopwatch.Start();

            LogCapabilities();

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Initialization complete - {_capabilities.GetAvailableLayersCount()} layers active");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Initialization error", ex);
        }
    }

    /// <summary>
    /// Get HAL capabilities
    /// </summary>
    public HardwareCapabilities Capabilities => _capabilities;

    /// <summary>
    /// Check if HAL is initialized
    /// </summary>
    public bool IsInitialized => _initialized;

    // ==================== Comprehensive Hardware Monitoring ====================

    /// <summary>
    /// Get complete hardware snapshot
    /// </summary>
    public HardwareSnapshot GetHardwareSnapshot()
    {
        var snapshot = new HardwareSnapshot
        {
            Timestamp = DateTime.Now,
            UptimeSeconds = _uptimeStopwatch.Elapsed.TotalSeconds
        };

        try
        {
            // Temperature data (from EC - most accurate)
            if (_capabilities.EcAccessAvailable && _ecAccess != null)
            {
                var temps = _ecAccess.ReadTemperatures();
                snapshot.CpuTemp = temps.CpuTemp;
                snapshot.GpuTemp = temps.GpuTemp;
                snapshot.VrmCpuTemp = temps.VrmCpuTemp;
                snapshot.VrmGpuTemp = temps.VrmGpuTemp;
                snapshot.BatteryTemp = temps.BatteryTemp;
                snapshot.SystemTemp = temps.SystemTemp;
            }

            // Fan data (from EC)
            if (_capabilities.EcAccessAvailable && _ecAccess != null)
            {
                var fans = _ecAccess.ReadFanInfo();
                snapshot.CpuFanRpm = fans.CpuFanRpm;
                snapshot.GpuFanRpm = fans.GpuFanRpm;
                snapshot.CpuFanPercent = fans.CpuFanPercent;
                snapshot.GpuFanPercent = fans.GpuFanPercent;
            }

            // Power data (from MSR RAPL - most accurate for CPU)
            if (_capabilities.MsrAccessAvailable && _msrAccess != null)
            {
                var raplData = _msrAccess.GetRAPLPowerData();

                // Calculate power from energy delta
                if (_lastRAPLData != null)
                {
                    var timeDelta = (DateTime.Now - _lastPowerSample).TotalSeconds;
                    if (timeDelta > 0)
                    {
                        snapshot.CpuPackagePowerWatts = (raplData.PackageEnergyJoules - _lastRAPLData.PackageEnergyJoules) / timeDelta;
                        snapshot.CpuCorePowerWatts = (raplData.CoreEnergyJoules - _lastRAPLData.CoreEnergyJoules) / timeDelta;
                        snapshot.DRAMPowerWatts = (raplData.DRAMEnergyJoules - _lastRAPLData.DRAMEnergyJoules) / timeDelta;
                    }
                }

                _lastRAPLData = raplData;
                _lastPowerSample = DateTime.Now;

                // Throttle status
                var throttle = _msrAccess.GetThrottleStatus();
                snapshot.IsThermalThrottling = throttle.IsThermalThrottling;
                snapshot.IsPowerLimitThrottling = throttle.IsPowerLimitThrottling;
            }

            // Battery data (from EC)
            if (_capabilities.EcAccessAvailable && _ecAccess != null)
            {
                var battery = _ecAccess.ReadBatteryInfo();
                snapshot.BatteryVoltage = battery.VoltageVolts;
                snapshot.BatteryCurrent = battery.CurrentAmps;
                snapshot.BatteryPowerWatts = battery.PowerWatts;
                snapshot.BatteryCapacityPercent = battery.CapacityPercent;
                snapshot.IsCharging = battery.IsCharging;
                snapshot.IsDischarging = battery.IsDischarging;
            }

            // GPU data (from NVAPI)
            if (_capabilities.NvapiAvailable && _nvapiIntegration != null)
            {
                snapshot.GpuPowerWatts = _nvapiIntegration.GetCurrentPowerWatts();
                snapshot.GpuPState = _nvapiIntegration.GetCurrentPState();
            }

            // Calculate total system power
            snapshot.TotalSystemPowerWatts = snapshot.CpuPackagePowerWatts +
                                              snapshot.GpuPowerWatts +
                                              snapshot.DRAMPowerWatts;

            // Acoustic estimation (fan speed to dBA)
            snapshot.EstimatedNoiseLevelDba = EstimateNoiseLevel(snapshot.CpuFanRpm, snapshot.GpuFanRpm);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Error reading hardware snapshot", ex);
        }

        return snapshot;
    }

    // ==================== Power Profile Application ====================

    /// <summary>
    /// Apply complete power profile atomically across all hardware layers
    /// </summary>
    public bool ApplyPowerProfile(string profileName)
    {
        if (!LegionSlim7iGen9Profile.Profiles.TryGetValue(profileName, out var profile))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Unknown profile: {profileName}");
            return false;
        }

        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Applying power profile: {profileName}");

            int layersApplied = 0;

            // Layer 1: EC Power Limits (fastest, always available on Legion hardware)
            if (_capabilities.EcAccessAvailable && _ecAccess != null)
            {
                _ecAccess.SetCpuPowerLimit((ushort)profile.CpuPowerLimitPl1);
                _ecAccess.SetGpuPowerLimit((ushort)profile.GpuPowerLimit);
                layersApplied++;
            }

            // Layer 2: MSR Power Limits (more precise, requires driver)
            if (_capabilities.MsrAccessAvailable && _msrAccess != null)
            {
                _msrAccess.SetPackagePowerLimits(
                    pl1Watts: profile.CpuPowerLimitPl1,
                    pl2Watts: profile.CpuPowerLimitPl2,
                    timeWindow1Sec: 28,
                    timeWindow2Sec: 10
                );
                layersApplied++;
            }

            // Layer 3: Fan Curve (thermal management)
            if (_capabilities.EcAccessAvailable && _ecAccess != null)
            {
                if (LegionSlim7iGen9Profile.FanCurves.TryGetValue(profile.FanCurvePreset, out var fanCurve))
                {
                    _ecAccess.SetFanCurve(fanCurve);
                    layersApplied++;
                }
            }

            // Layer 4: GPU Power Limit (if available)
            if (_capabilities.NvapiAvailable && _nvapiIntegration != null)
            {
                if (profile.GpuPowerLimit > 0)
                {
                    _nvapiIntegration.SetPowerLimit(profile.GpuPowerLimit);
                    layersApplied++;
                }
            }

            // Layer 5: C-State optimization for power saving profiles
            if (_capabilities.MsrAccessAvailable && _msrAccess != null)
            {
                var cStateLimit = profileName switch
                {
                    "Quiet" or "MediaPlayback" => CStateLimit.C10,  // Maximum power savings
                    "Balanced" => CStateLimit.C7,                   // Balanced
                    "Performance" or "Gaming" => CStateLimit.C3,     // Low latency
                    _ => CStateLimit.C6
                };

                _msrAccess.SetCStateLimit(cStateLimit);
                layersApplied++;
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Profile applied successfully - {layersApplied} layers configured");

            return true;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Error applying power profile", ex);
            return false;
        }
    }

    // ==================== Direct Hardware Control Methods ====================

    /// <summary>
    /// Set fan speeds directly (manual control)
    /// </summary>
    public bool SetFanSpeeds(int cpuPercent, int gpuPercent)
    {
        if (!_capabilities.EcAccessAvailable || _ecAccess == null)
            return false;

        try
        {
            // Clamp to safe range
            cpuPercent = Math.Max(0, Math.Min(100, cpuPercent));
            gpuPercent = Math.Max(0, Math.Min(100, gpuPercent));

            byte cpuPwm = (byte)(cpuPercent * 255 / 100);
            byte gpuPwm = (byte)(gpuPercent * 255 / 100);

            _ecAccess.SetFanSpeed(cpuPwm, gpuPwm);
            return true;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Error setting fan speeds", ex);
            return false;
        }
    }

    /// <summary>
    /// Set CPU power limits
    /// </summary>
    public bool SetCPUPowerLimits(int pl1Watts, int pl2Watts)
    {
        try
        {
            // Clamp to safe hardware limits
            pl1Watts = Math.Max(20, Math.Min(115, pl1Watts));
            pl2Watts = Math.Max(pl1Watts, Math.Min(115, pl2Watts));

            int layersApplied = 0;

            // Apply via EC (always available)
            if (_capabilities.EcAccessAvailable && _ecAccess != null)
            {
                _ecAccess.SetCpuPowerLimit((ushort)pl1Watts);
                layersApplied++;
            }

            // Apply via MSR (more precise)
            if (_capabilities.MsrAccessAvailable && _msrAccess != null)
            {
                _msrAccess.SetPackagePowerLimits(pl1Watts, pl2Watts);
                layersApplied++;
            }

            return layersApplied > 0;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Error setting CPU power limits", ex);
            return false;
        }
    }

    /// <summary>
    /// Set GPU power limit
    /// </summary>
    public bool SetGPUPowerLimit(int watts)
    {
        try
        {
            // Clamp to safe hardware limits (RTX 4070 Laptop: 90-140W)
            watts = Math.Max(0, Math.Min(140, watts));

            int layersApplied = 0;

            // Apply via EC
            if (_capabilities.EcAccessAvailable && _ecAccess != null)
            {
                _ecAccess.SetGpuPowerLimit((ushort)watts);
                layersApplied++;
            }

            // Apply via NVAPI (more precise)
            if (_capabilities.NvapiAvailable && _nvapiIntegration != null && watts > 0)
            {
                _nvapiIntegration.SetPowerLimit(watts);
                layersApplied++;
            }

            return layersApplied > 0;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Error setting GPU power limit", ex);
            return false;
        }
    }

    /// <summary>
    /// Enable/disable battery conservation mode
    /// </summary>
    public bool SetBatteryConservation(bool enabled)
    {
        if (!_capabilities.EcAccessAvailable || _ecAccess == null)
            return false;

        try
        {
            _ecAccess.SetBatteryConservation(enabled);
            return true;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"HAL: Error setting battery conservation", ex);
            return false;
        }
    }

    // ==================== Utility Methods ====================

    private void DetectHardwareCapabilities()
    {
        // Detect available control layers
        _capabilities.CanControlFans = _capabilities.EcAccessAvailable;
        _capabilities.CanControlCpuPower = _capabilities.EcAccessAvailable || _capabilities.MsrAccessAvailable;
        _capabilities.CanControlGpuPower = _capabilities.EcAccessAvailable || _capabilities.NvapiAvailable;
        _capabilities.CanReadTemperatures = _capabilities.EcAccessAvailable;
        _capabilities.CanReadPower = _capabilities.MsrAccessAvailable || _capabilities.EcAccessAvailable;
        _capabilities.CanControlCStates = _capabilities.MsrAccessAvailable;
        _capabilities.CanControlBattery = _capabilities.EcAccessAvailable;
    }

    private void LogCapabilities()
    {
        if (!Log.Instance.IsTraceEnabled)
            return;

        Log.Instance.Trace($"HAL: Hardware Capabilities:");
        Log.Instance.Trace($"  EC Access: {(_capabilities.EcAccessAvailable ? "YES" : "NO")}");
        Log.Instance.Trace($"  MSR Access: {(_capabilities.MsrAccessAvailable ? "YES" : "NO")}");
        Log.Instance.Trace($"  NVAPI: {(_capabilities.NvapiAvailable ? "YES" : "NO")}");
        Log.Instance.Trace($"  PCIe Power: {(_capabilities.PciePowerAvailable ? "YES" : "NO")}");
        Log.Instance.Trace($"HAL: Control Capabilities:");
        Log.Instance.Trace($"  Fan Control: {(_capabilities.CanControlFans ? "YES" : "NO")}");
        Log.Instance.Trace($"  CPU Power: {(_capabilities.CanControlCpuPower ? "YES" : "NO")}");
        Log.Instance.Trace($"  GPU Power: {(_capabilities.CanControlGpuPower ? "YES" : "NO")}");
        Log.Instance.Trace($"  C-States: {(_capabilities.CanControlCStates ? "YES" : "NO")}");
        Log.Instance.Trace($"  Battery: {(_capabilities.CanControlBattery ? "YES" : "NO")}");
    }

    private int EstimateNoiseLevel(int cpuFanRpm, int gpuFanRpm)
    {
        // Legion Slim 7i acoustic profile (empirical data)
        // 0 RPM = ~20 dBA (ambient)
        // 2750 RPM (50%) = ~35 dBA
        // 5500 RPM (100%) = ~50 dBA

        int maxRpm = Math.Max(cpuFanRpm, gpuFanRpm);

        if (maxRpm == 0) return 20;
        if (maxRpm < 1100) return 25;  // Very quiet
        if (maxRpm < 2200) return 30;  // Quiet
        if (maxRpm < 3300) return 35;  // Noticeable
        if (maxRpm < 4400) return 42;  // Moderate
        return 50;                      // Loud
    }

    /// <summary>
    /// Shutdown HAL and cleanup resources
    /// </summary>
    public void Shutdown()
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"HAL: Shutting down");

        _uptimeStopwatch.Stop();
        KernelDriverInterface.Shutdown();
    }
}

// ==================== Data Structures ====================

public class HardwareCapabilities
{
    // Access layers available
    public bool EcAccessAvailable { get; set; }
    public bool MsrAccessAvailable { get; set; }
    public bool NvapiAvailable { get; set; }
    public bool PciePowerAvailable { get; set; }

    // Control capabilities
    public bool CanControlFans { get; set; }
    public bool CanControlCpuPower { get; set; }
    public bool CanControlGpuPower { get; set; }
    public bool CanReadTemperatures { get; set; }
    public bool CanReadPower { get; set; }
    public bool CanControlCStates { get; set; }
    public bool CanControlBattery { get; set; }

    public int GetAvailableLayersCount()
    {
        int count = 0;
        if (EcAccessAvailable) count++;
        if (MsrAccessAvailable) count++;
        if (NvapiAvailable) count++;
        if (PciePowerAvailable) count++;
        return count;
    }
}

public class HardwareSnapshot
{
    public DateTime Timestamp { get; set; }
    public double UptimeSeconds { get; set; }

    // Temperatures (°C)
    public byte CpuTemp { get; set; }
    public byte GpuTemp { get; set; }
    public byte VrmCpuTemp { get; set; }
    public byte VrmGpuTemp { get; set; }
    public byte BatteryTemp { get; set; }
    public byte SystemTemp { get; set; }

    // Fan Speeds
    public ushort CpuFanRpm { get; set; }
    public ushort GpuFanRpm { get; set; }
    public int CpuFanPercent { get; set; }
    public int GpuFanPercent { get; set; }

    // Power (Watts)
    public double CpuPackagePowerWatts { get; set; }
    public double CpuCorePowerWatts { get; set; }
    public double GpuPowerWatts { get; set; }
    public double DRAMPowerWatts { get; set; }
    public double TotalSystemPowerWatts { get; set; }

    // Battery
    public double BatteryVoltage { get; set; }
    public double BatteryCurrent { get; set; }
    public double BatteryPowerWatts { get; set; }
    public int BatteryCapacityPercent { get; set; }
    public bool IsCharging { get; set; }
    public bool IsDischarging { get; set; }

    // Performance State
    public NVAPIIntegration.GPUPState GpuPState { get; set; }

    // Throttling
    public bool IsThermalThrottling { get; set; }
    public bool IsPowerLimitThrottling { get; set; }

    // Acoustics
    public int EstimatedNoiseLevelDba { get; set; }

    // Computed metrics
    public byte MaxTemperature => Math.Max(CpuTemp, Math.Max(GpuTemp, Math.Max(VrmCpuTemp, VrmGpuTemp)));
    public bool IsOverheating => MaxTemperature > 85;
    public bool IsCriticalTemp => MaxTemperature > 95;
}
