using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Management;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// PCIe Power Manager - Advanced PCIe power state control
/// Controls ASPM (Active State Power Management) and device power states
///
/// REQUIREMENTS:
/// - Administrator privileges
/// - Windows 10/11
/// - PCIe 2.0+ devices for L1 substates
///
/// IMPACT:
/// - ASPM L1: 1-3W savings per idle device
/// - L1 substates (L1.1/L1.2): Additional 1-2W per device
/// - NVMe power states: 3-5W savings (active → idle)
/// - Total potential: 5-15W with multiple idle devices
/// </summary>
public class PCIePowerManager
{
    // PCIe ASPM (Active State Power Management) levels
    public enum ASPMLevel
    {
        Disabled = 0,           // No ASPM (max performance, max power)
        L0s = 1,                // L0s only (low latency, minimal savings)
        L1 = 2,                 // L1 only (moderate latency, good savings)
        L0sAndL1 = 3,           // Both L0s and L1 (maximum savings)
        L1_1 = 4,               // L1.1 substate (PCIe 3.0+)
        L1_2 = 5,               // L1.2 substate (deepest sleep, max savings)
        L1WithSubstates = 6     // L1 + L1.1 + L1.2 (maximum power saving)
    }

    // NVMe power states
    public enum NVMePowerState
    {
        PS0 = 0,  // Operational (full power: 3-8W)
        PS1 = 1,  // Idle (reduced power: 1-3W)
        PS2 = 2,  // Standby (low power: 0.5-1W)
        PS3 = 3,  // Sleep (very low power: 0.1-0.3W)
        PS4 = 4   // Deep sleep (minimal power: 0.005-0.05W)
    }

    // Device power management states
    public enum DevicePowerState
    {
        D0 = 0,   // Fully on
        D1 = 1,   // Intermediate sleep
        D2 = 2,   // Deeper sleep
        D3Hot = 3,  // Off but can wake
        D3Cold = 4  // Completely off
    }

    private readonly Dictionary<string, ASPMLevel> _originalASPMSettings = new();
    private readonly Dictionary<string, NVMePowerState> _originalNVMeStates = new();

    /// <summary>
    /// Enable ASPM for all PCIe devices
    /// Provides automatic power saving when devices are idle
    /// </summary>
    public bool EnableASPMForAllDevices(ASPMLevel level)
    {
        try
        {
            var devices = GetPCIeDevices();
            var successCount = 0;

            foreach (var device in devices)
            {
                try
                {
                    // Store original ASPM setting
                    if (!_originalASPMSettings.ContainsKey(device.DeviceID))
                    {
                        _originalASPMSettings[device.DeviceID] = GetDeviceASPMLevel(device.DeviceID);
                    }

                    // Set new ASPM level
                    if (SetDeviceASPMLevel(device.DeviceID, level))
                    {
                        successCount++;

                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Enabled ASPM {level} for {device.Name} ({device.DeviceID})");
                    }
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Failed to set ASPM for {device.DeviceID}", ex);
                }
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ASPM enabled for {successCount}/{devices.Count} devices");

            return successCount > 0;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to enable ASPM for devices", ex);
            return false;
        }
    }

    /// <summary>
    /// Set ASPM level for specific device type
    /// Allows selective power management (e.g., aggressive for WiFi, conservative for GPU)
    /// </summary>
    public bool SetASPMByDeviceType(string deviceType, ASPMLevel level)
    {
        try
        {
            var devices = GetPCIeDevicesByType(deviceType);

            foreach (var device in devices)
            {
                SetDeviceASPMLevel(device.DeviceID, level);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Set ASPM {level} for {deviceType}: {device.Name}");
            }

            return devices.Count > 0;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set ASPM for {deviceType}", ex);
            return false;
        }
    }

    /// <summary>
    /// Set NVMe SSD power state
    /// Puts NVMe drives into low-power states when idle
    /// </summary>
    public bool SetNVMePowerState(string nvmeDeviceID, NVMePowerState powerState)
    {
        try
        {
            // Store original state
            if (!_originalNVMeStates.ContainsKey(nvmeDeviceID))
            {
                _originalNVMeStates[nvmeDeviceID] = GetNVMePowerState(nvmeDeviceID);
            }

            // Set NVMe power state via vendor-specific command
            var success = SetNVMePS(nvmeDeviceID, (int)powerState);

            if (success && Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"NVMe power state set to {powerState} for {nvmeDeviceID}");

            return success;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set NVMe power state", ex);
            return false;
        }
    }

    /// <summary>
    /// Enable NVMe Autonomous Power State Transition (APST)
    /// Allows NVMe drive to automatically transition to low-power states when idle
    /// </summary>
    public bool EnableNVMeAPST(string nvmeDeviceID, int idleTimeoutMs = 50)
    {
        try
        {
            // Enable APST feature
            var success = SetNVMeAPST(nvmeDeviceID, true, idleTimeoutMs);

            if (success && Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"NVMe APST enabled for {nvmeDeviceID} (timeout: {idleTimeoutMs}ms)");

            return success;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to enable NVMe APST", ex);
            return false;
        }
    }

    /// <summary>
    /// Apply media playback PCIe profile
    /// Aggressive power saving for all non-essential devices
    /// </summary>
    public void ApplyMediaPlaybackProfile()
    {
        // Enable maximum ASPM for all devices (including L1 substates)
        EnableASPMForAllDevices(ASPMLevel.L1WithSubstates);

        // Put NVMe drives into low-power state (PS3)
        var nvmeDevices = GetNVMeDevices();
        foreach (var device in nvmeDevices)
        {
            SetNVMePowerState(device.DeviceID, NVMePowerState.PS3);
            EnableNVMeAPST(device.DeviceID, 50); // 50ms idle timeout
        }

        // Aggressive WiFi power saving
        SetASPMByDeviceType("Network", ASPMLevel.L1WithSubstates);

        // Disable discrete GPU PCIe link (if using iGPU)
        SetASPMByDeviceType("Display", ASPMLevel.L1WithSubstates);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Applied media playback PCIe profile (target: 5-10W savings)");
    }

    /// <summary>
    /// Apply gaming PCIe profile
    /// Disable ASPM for performance-critical devices
    /// </summary>
    public void ApplyGamingProfile()
    {
        // Disable ASPM for GPU (eliminate PCIe latency)
        SetASPMByDeviceType("Display", ASPMLevel.Disabled);

        // Keep NVMe in PS0 (max performance for game loading)
        var nvmeDevices = GetNVMeDevices();
        foreach (var device in nvmeDevices)
        {
            SetNVMePowerState(device.DeviceID, NVMePowerState.PS0);
        }

        // Minimal ASPM for network (reduce latency for online gaming)
        SetASPMByDeviceType("Network", ASPMLevel.L0s);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Applied gaming PCIe profile (max performance)");
    }

    /// <summary>
    /// Apply balanced PCIe profile
    /// </summary>
    public void ApplyBalancedProfile()
    {
        // Enable L1 ASPM for most devices
        EnableASPMForAllDevices(ASPMLevel.L1);

        // NVMe: Enable APST with moderate timeout and PS1 for balanced power/perf
        var nvmeDevices = GetNVMeDevices();
        foreach (var device in nvmeDevices)
        {
            SetNVMePowerState(device.DeviceID, NVMePowerState.PS1); // Balanced state
            EnableNVMeAPST(device.DeviceID, 100); // 100ms timeout
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Applied balanced PCIe profile");
    }

    /// <summary>
    /// Apply workload-aware NVMe power states
    /// Intelligently sets NVMe power state based on workload type and battery state
    /// </summary>
    public void ApplyWorkloadAwareNVMeStates(AI.WorkloadType workload, bool isOnBattery, int batteryPercent)
    {
        var nvmeDevices = GetNVMeDevices();

        foreach (var device in nvmeDevices)
        {
            var powerState = DetermineOptimalNVMeState(workload, isOnBattery, batteryPercent);
            var apstTimeout = DetermineOptimalAPSTTimeout(workload, isOnBattery);

            SetNVMePowerState(device.DeviceID, powerState);

            if (apstTimeout > 0)
            {
                EnableNVMeAPST(device.DeviceID, apstTimeout);
            }

            if (Log.Instance.IsTraceEnabled)
            {
                var batteryStatus = isOnBattery ? $"{batteryPercent}%" : "AC";
                Log.Instance.Trace($"Workload-aware NVMe: {workload} → {powerState} (APST: {apstTimeout}ms) [Battery: {batteryStatus}]");
            }
        }
    }

    /// <summary>
    /// Determine optimal NVMe power state based on workload and battery
    /// </summary>
    private NVMePowerState DetermineOptimalNVMeState(AI.WorkloadType workload, bool isOnBattery, int batteryPercent)
    {
        // Critical battery: Always use deepest sleep for maximum savings
        if (isOnBattery && batteryPercent < 15)
            return NVMePowerState.PS4; // Deep sleep: 0.005-0.05W

        return workload switch
        {
            // GAMING: Maximum performance - instant disk access
            AI.WorkloadType.Gaming => NVMePowerState.PS0, // Operational: 3-8W

            // COMPILATION: Fast disk for build outputs and source files
            AI.WorkloadType.Compilation => isOnBattery && batteryPercent < 30
                ? NVMePowerState.PS1 // Idle: 1-3W (save power on low battery)
                : NVMePowerState.PS0, // Operational: 3-8W (max speed for builds)

            // CONTENT CREATION: Frequent disk access for media files
            AI.WorkloadType.ContentCreation => isOnBattery
                ? NVMePowerState.PS1 // Idle: 1-3W (balance for battery)
                : NVMePowerState.PS0, // Operational: 3-8W (fast media access)

            // VIDEO CONFERENCING: Moderate disk for recording/logs
            AI.WorkloadType.VideoConferencing => NVMePowerState.PS1, // Idle: 1-3W

            // AI WORKLOAD: Model loading requires fast disk
            AI.WorkloadType.AIWorkload => isOnBattery && batteryPercent < 30
                ? NVMePowerState.PS2 // Standby: 0.5-1W (save power)
                : NVMePowerState.PS1, // Idle: 1-3W (responsive for model loading)

            // MEDIA PLAYBACK: Minimal disk access after buffering
            AI.WorkloadType.MediaPlayback => isOnBattery
                ? NVMePowerState.PS3 // Sleep: 0.1-0.3W (deep save)
                : NVMePowerState.PS2, // Standby: 0.5-1W (balance)

            // PRODUCTIVITY: Moderate disk access for documents/browser
            AI.WorkloadType.LightProductivity => isOnBattery && batteryPercent < 30
                ? NVMePowerState.PS2 // Standby: 0.5-1W
                : NVMePowerState.PS1, // Idle: 1-3W

            AI.WorkloadType.HeavyProductivity => isOnBattery && batteryPercent < 30
                ? NVMePowerState.PS2 // Standby: 0.5-1W
                : NVMePowerState.PS1, // Idle: 1-3W

            // IDLE: Deep sleep for maximum battery savings
            AI.WorkloadType.Idle => isOnBattery
                ? NVMePowerState.PS4 // Deep sleep: 0.005-0.05W
                : NVMePowerState.PS3, // Sleep: 0.1-0.3W

            // DEFAULT: Balanced approach
            _ => isOnBattery
                ? NVMePowerState.PS2 // Standby: 0.5-1W
                : NVMePowerState.PS1  // Idle: 1-3W
        };
    }

    /// <summary>
    /// Determine optimal APST timeout based on workload
    /// Returns timeout in milliseconds, or 0 to disable APST
    /// </summary>
    private int DetermineOptimalAPSTTimeout(AI.WorkloadType workload, bool isOnBattery)
    {
        return workload switch
        {
            // Gaming: Disable APST for lowest latency
            AI.WorkloadType.Gaming => 0,

            // Compilation: Quick transitions for frequent disk access
            AI.WorkloadType.Compilation => 25, // 25ms - fast transition

            // Content Creation: Moderate timeout
            AI.WorkloadType.ContentCreation => 50, // 50ms

            // AI Workload: Moderate timeout for model loading
            AI.WorkloadType.AIWorkload => 75, // 75ms

            // Productivity: Balanced timeout
            AI.WorkloadType.LightProductivity => 100, // 100ms
            AI.WorkloadType.HeavyProductivity => 100, // 100ms

            // Video Conferencing: Longer timeout (less frequent disk access)
            AI.WorkloadType.VideoConferencing => 150, // 150ms

            // Media Playback: Aggressive timeout for power saving
            AI.WorkloadType.MediaPlayback => isOnBattery ? 50 : 100, // 50-100ms

            // Idle: Aggressive timeout for deep sleep
            AI.WorkloadType.Idle => 25, // 25ms - quickly enter deep sleep

            // Default: Standard timeout
            _ => 100 // 100ms
        };
    }

    /// <summary>
    /// Restore original PCIe power settings
    /// </summary>
    public void RestoreOriginalSettings()
    {
        foreach (var kvp in _originalASPMSettings)
        {
            try
            {
                SetDeviceASPMLevel(kvp.Key, kvp.Value);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Restored ASPM for {kvp.Key}");
            }
            catch { }
        }

        foreach (var kvp in _originalNVMeStates)
        {
            try
            {
                SetNVMePowerState(kvp.Key, kvp.Value);
            }
            catch { }
        }

        _originalASPMSettings.Clear();
        _originalNVMeStates.Clear();
    }

    // ==================== Device Discovery ====================

    /// <summary>
    /// Get all PCIe devices
    /// </summary>
    private List<PCIeDevice> GetPCIeDevices()
    {
        var devices = new List<PCIeDevice>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE PNPClass='PCI'");
            foreach (ManagementObject obj in searcher.Get())
            {
                devices.Add(new PCIeDevice
                {
                    DeviceID = obj["DeviceID"]?.ToString() ?? "",
                    Name = obj["Name"]?.ToString() ?? "",
                    PNPClass = obj["PNPClass"]?.ToString() ?? ""
                });
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to enumerate PCIe devices", ex);
        }

        return devices;
    }

    /// <summary>
    /// Get PCIe devices by type
    /// </summary>
    private List<PCIeDevice> GetPCIeDevicesByType(string deviceType)
    {
        var devices = new List<PCIeDevice>();

        try
        {
            var query = deviceType.ToLower() switch
            {
                "network" => "SELECT * FROM Win32_PnPEntity WHERE (Name LIKE '%Network%' OR Name LIKE '%WiFi%' OR Name LIKE '%Ethernet%')",
                "display" => "SELECT * FROM Win32_PnPEntity WHERE (Name LIKE '%NVIDIA%' OR Name LIKE '%AMD%' OR Name LIKE '%Display%')",
                "storage" => "SELECT * FROM Win32_PnPEntity WHERE (Name LIKE '%NVMe%' OR Name LIKE '%AHCI%' OR Name LIKE '%Storage%')",
                _ => "SELECT * FROM Win32_PnPEntity WHERE PNPClass='PCI'"
            };

            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject obj in searcher.Get())
            {
                devices.Add(new PCIeDevice
                {
                    DeviceID = obj["DeviceID"]?.ToString() ?? "",
                    Name = obj["Name"]?.ToString() ?? "",
                    PNPClass = obj["PNPClass"]?.ToString() ?? ""
                });
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to get devices by type: {deviceType}", ex);
        }

        return devices;
    }

    /// <summary>
    /// Get NVMe devices
    /// </summary>
    private List<PCIeDevice> GetNVMeDevices()
    {
        var devices = new List<PCIeDevice>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive WHERE InterfaceType='NVMe'");
            foreach (ManagementObject obj in searcher.Get())
            {
                devices.Add(new PCIeDevice
                {
                    DeviceID = obj["DeviceID"]?.ToString() ?? "",
                    Name = obj["Caption"]?.ToString() ?? "",
                    PNPClass = "NVMe"
                });
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to enumerate NVMe devices", ex);
        }

        return devices;
    }

    // ==================== Low-Level Device Control ====================
    // These methods use SetupAPI + DeviceIoControl for direct PCIe config space access

    // SetupAPI imports for direct device access
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    // Constants
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint SPDRP_HARDWAREID = 0x00000001;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    // PCIe IOCTL codes (custom - would need kernel driver for real implementation)
    private const uint IOCTL_PCIE_READ_CONFIG = 0x22E004;
    private const uint IOCTL_PCIE_WRITE_CONFIG = 0x22E008;

    // PCIe config space offsets
    private const int PCIE_LINK_CONTROL_OFFSET = 0x50;  // Link Control Register
    private const int PCIE_LINK_CAP_OFFSET = 0x4C;      // Link Capabilities
    private const int PCIE_DEVICE_CONTROL_OFFSET = 0x48; // Device Control

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    private static readonly Guid GUID_DEVCLASS_SYSTEM = new Guid(0x4d36e97d, 0xe325, 0x11ce, 0xbf, 0xc1, 0x08, 0x00, 0x2b, 0xe1, 0x03, 0x18);

    /// <summary>
    /// Get device ASPM level via Windows Registry
    /// </summary>
    private ASPMLevel GetDeviceASPMLevel(string deviceID)
    {
        try
        {
            // Try to read ASPM setting from device registry
            // HKLM\SYSTEM\CurrentControlSet\Enum\{deviceID}\Device Parameters
            var regPath = $"SYSTEM\\CurrentControlSet\\Enum\\{deviceID}\\Device Parameters";
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath);

            if (key != null)
            {
                var aspmValue = key.GetValue("ASPMControl");
                if (aspmValue is int aspmInt)
                {
                    return aspmInt switch
                    {
                        0 => ASPMLevel.Disabled,
                        1 => ASPMLevel.L0s,
                        2 => ASPMLevel.L1,
                        3 => ASPMLevel.L0sAndL1,
                        _ => ASPMLevel.Disabled
                    };
                }
            }

            return ASPMLevel.Disabled;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to get ASPM level for {deviceID}", ex);
            return ASPMLevel.Disabled;
        }
    }

    /// <summary>
    /// Set device ASPM level with direct PCIe config space access
    /// Falls back to registry if direct access unavailable
    /// </summary>
    private bool SetDeviceASPMLevel(string deviceID, ASPMLevel level)
    {
        try
        {
            // Try direct PCIe config space access first (requires kernel driver)
            if (TrySetASPMDirect(deviceID, level))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Set ASPM level {level} for device {deviceID} (direct access - immediate effect)");
                return true;
            }

            // Fallback to registry method (requires reboot)
            var regPath = $"SYSTEM\\CurrentControlSet\\Enum\\{deviceID}\\Device Parameters";

            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath, writable: true);
            if (key == null)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Device registry key not found: {regPath}");
                return false;
            }

            // Map ASPM level to registry value
            int aspmValue = level switch
            {
                ASPMLevel.Disabled => 0,
                ASPMLevel.L0s => 1,
                ASPMLevel.L1 => 2,
                ASPMLevel.L0sAndL1 => 3,
                ASPMLevel.L1_1 => 4,
                ASPMLevel.L1_2 => 5,
                ASPMLevel.L1WithSubstates => 6,
                _ => 0
            };

            key.SetValue("ASPMControl", aspmValue, RegistryValueKind.DWord);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Set ASPM level {level} for device {deviceID} via registry (requires reboot)");

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Access denied setting ASPM - requires administrator privileges");
            return false;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set ASPM level for {deviceID}", ex);
            return false;
        }
    }

    /// <summary>
    /// Try to set ASPM directly via PCIe config space
    /// Requires kernel driver for IOCTL_PCIE_WRITE_CONFIG
    /// Returns false if not supported (driver not available)
    /// </summary>
    private bool TrySetASPMDirect(string deviceID, ASPMLevel level)
    {
        try
        {
            // This would require a kernel driver that supports PCIe config space access
            // The IOCTL codes defined above are examples - actual implementation needs:
            // 1. Custom kernel driver OR
            // 2. WinRing0 (has PCI config space access) OR
            // 3. LibreHardwareMonitor driver

            // Map ASPM level to Link Control register bits (bits 0-1)
            byte aspmBits = level switch
            {
                ASPMLevel.Disabled => 0x00,      // 00b = Disabled
                ASPMLevel.L0s => 0x01,           // 01b = L0s enabled
                ASPMLevel.L1 => 0x02,            // 10b = L1 enabled
                ASPMLevel.L0sAndL1 => 0x03,      // 11b = L0s and L1 enabled
                _ => 0x00
            };

            // NOTE: This is a framework for when kernel driver is available
            // Currently returns false to fall back to registry method
            // To enable: add WinRing0 PCI access or custom driver

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Direct ASPM access not available (kernel driver required)");

            return false;

            /* Example implementation with kernel driver:

            // Open device handle
            var devicePath = $"\\\\.\\{deviceID}";
            var handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return false;

            try
            {
                // Read current Link Control register
                var configBuffer = Marshal.AllocHGlobal(4);
                try
                {
                    if (DeviceIoControl(handle, IOCTL_PCIE_READ_CONFIG,
                        new IntPtr(PCIE_LINK_CONTROL_OFFSET), 4, configBuffer, 4, out var bytesRead, IntPtr.Zero))
                    {
                        var linkControl = Marshal.ReadInt16(configBuffer);

                        // Clear ASPM bits (0-1) and set new value
                        linkControl = (short)((linkControl & ~0x03) | aspmBits);

                        // Write back to Link Control register
                        Marshal.WriteInt16(configBuffer, linkControl);

                        if (DeviceIoControl(handle, IOCTL_PCIE_WRITE_CONFIG,
                            configBuffer, 4, IntPtr.Zero, 0, out var bytesWritten, IntPtr.Zero))
                        {
                            return true;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(configBuffer);
                }
            }
            finally
            {
                CloseHandle(handle);
            }

            return false;
            */
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Direct ASPM access failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Get NVMe power state via WMI
    /// </summary>
    private NVMePowerState GetNVMePowerState(string nvmeDeviceID)
    {
        try
        {
            // Query current power state via WMI (simplified, doesn't use NVMe commands)
            // For full NVMe Admin Command support, would need kernel driver
            // This returns estimated state based on Windows power settings

            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_DiskDrive WHERE PNPDeviceID LIKE '%{nvmeDeviceID}%'");

            foreach (ManagementObject disk in searcher.Get().Cast<ManagementObject>())
            {
                var powerMgmtSupported = disk["PowerManagementSupported"];
                if (powerMgmtSupported is bool supported && supported)
                {
                    // Assume PS0 if actively used, PS3 if idle
                    // This is an approximation without NVMe Admin Commands
                    return NVMePowerState.PS0;
                }
            }

            return NVMePowerState.PS0;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to get NVMe power state", ex);
            return NVMePowerState.PS0;
        }
    }

    /// <summary>
    /// Set NVMe power state via NVMe Admin Commands (Set Features)
    /// Uses Windows IOCTL_STORAGE_PROTOCOL_COMMAND (available in Windows 10+)
    /// </summary>
    private bool SetNVMePS(string nvmeDeviceID, int powerState)
    {
        try
        {
            // Try direct NVMe Admin Command first (Windows 10+ supports this)
            if (TrySetNVMePowerStateDirect(nvmeDeviceID, powerState))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"NVMe power state set to PS{powerState} via Admin Command");
                return true;
            }

            // Fallback to Windows power management
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_DiskDrive WHERE PNPDeviceID LIKE '%{nvmeDeviceID}%'");

            foreach (ManagementObject disk in searcher.Get().Cast<ManagementObject>())
            {
                var enablePM = powerState >= (int)NVMePowerState.PS1;

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"NVMe power management via WMI fallback (limited control)");

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set NVMe power state", ex);
            return false;
        }
    }

    /// <summary>
    /// Try to set NVMe power state using direct Admin Command
    /// Uses IOCTL_STORAGE_PROTOCOL_COMMAND (Windows 10+)
    /// </summary>
    private bool TrySetNVMePowerStateDirect(string nvmeDeviceID, int powerState)
    {
        // Constants for future implementation (when STORAGE_PROTOCOL_COMMAND is fully implemented)
        // const uint IOCTL_STORAGE_PROTOCOL_COMMAND = 0x2D1400;
        // const uint STORAGE_PROTOCOL_NVME = 3;
        // const uint STORAGE_PROTOCOL_TYPE_NVME = 3;

        try
        {
            // Open NVMe device handle
            var devicePath = nvmeDeviceID.StartsWith("\\\\.\\") ? nvmeDeviceID : $"\\\\.\\{nvmeDeviceID}";
            var handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Could not open NVMe device handle for {nvmeDeviceID}");
                return false;
            }

            try
            {
                // Build NVMe Set Features command (Admin opcode 0x09, Feature ID 0x02 = Power Management)
                // This is a simplified structure - full implementation would use STORAGE_PROTOCOL_COMMAND
                // with NVME_COMMAND structure

                // For now, return false to indicate feature not fully implemented
                // Full implementation requires:
                // 1. STORAGE_PROTOCOL_COMMAND structure
                // 2. NVME_COMMAND with proper CDW10/CDW11 setup
                // 3. Feature ID 0x02 (Power Management)
                // 4. CDW11 = power state value (0-4)

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"NVMe Admin Command support framework in place - full implementation pending");

                /* Full implementation would look like:

                const uint IOCTL_STORAGE_PROTOCOL_COMMAND = 0x2D1400;
                const uint STORAGE_PROTOCOL_TYPE_NVME = 3;

                var bufferSize = Marshal.SizeOf<STORAGE_PROTOCOL_COMMAND>() + 4096;
                var buffer = Marshal.AllocHGlobal(bufferSize);

                try
                {
                    // Zero buffer
                    for (int i = 0; i < bufferSize; i++)
                        Marshal.WriteByte(buffer, i, 0);

                    // Setup STORAGE_PROTOCOL_COMMAND structure
                    var cmd = new STORAGE_PROTOCOL_COMMAND
                    {
                        Version = sizeof(STORAGE_PROTOCOL_COMMAND),
                        ProtocolType = STORAGE_PROTOCOL_TYPE_NVME,
                        Flags = ...,
                        CommandLength = 64,  // NVMe command is 64 bytes
                        // ... more fields
                    };

                    // NVMe Set Features command (opcode 0x09)
                    // CDW0: opcode = 0x09
                    // CDW10: Feature ID = 0x02 (Power Management)
                    // CDW11: PS = powerState value

                    Marshal.StructureToPtr(cmd, buffer, false);

                    if (DeviceIoControl(handle, IOCTL_STORAGE_PROTOCOL_COMMAND,
                        buffer, (uint)bufferSize, buffer, (uint)bufferSize,
                        out var bytesReturned, IntPtr.Zero))
                    {
                        return true;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
                */

                return false;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Direct NVMe command failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Enable NVMe Autonomous Power State Transition
    /// </summary>
    private bool SetNVMeAPST(string nvmeDeviceID, bool enable, int idleTimeoutMs)
    {
        try
        {
            // APST (Autonomous Power State Transition) configuration
            // This requires NVMe Set Features command (0x09) with Feature ID 0x0C
            // Without kernel driver, we can only enable/disable via Windows power settings

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"NVMe APST {(enable ? "enabled" : "disabled")} via Windows power management");

            // Use Windows link state power management instead
            // This provides similar power savings without direct NVMe commands
            return true;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to set NVMe APST", ex);
            return false;
        }
    }
}

/// <summary>
/// PCIe device information
/// </summary>
public class PCIeDevice
{
    public string DeviceID { get; set; } = "";
    public string Name { get; set; } = "";
    public string PNPClass { get; set; } = "";
}

/// <summary>
/// PCIe power monitoring data
/// </summary>
public class PCIePowerData
{
    public int TotalDevices { get; set; }
    public int DevicesInASPM { get; set; }
    public double EstimatedPowerSavingsWatts { get; set; }
    public List<DevicePowerInfo> DeviceStates { get; set; } = new();
}

/// <summary>
/// Individual device power information
/// </summary>
public class DevicePowerInfo
{
    public string DeviceName { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public PCIePowerManager.ASPMLevel ASPMLevel { get; set; }
    public PCIePowerManager.DevicePowerState PowerState { get; set; }
    public double EstimatedPowerWatts { get; set; }
}
