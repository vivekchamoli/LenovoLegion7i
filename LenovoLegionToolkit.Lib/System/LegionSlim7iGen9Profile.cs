using System;
using System.Collections.Generic;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// Lenovo Legion Slim 7i Gen 9 (2024) Hardware Profile
/// Model: 16IRH8 / 16IAH8
///
/// SPECIFICATIONS:
/// - CPU: Intel Core Ultra 9 185H / i9-14900HX
/// - GPU: NVIDIA RTX 4070 Laptop (140W TGP)
/// - RAM: Up to 32GB LPDDR5X-7467
/// - Display: 16" 3.2K 165Hz / 2.5K 240Hz
/// - Battery: 99.9Wh
/// - Cooling: Dual fan with vapor chamber
/// - EC: ITE IT5570E
///
/// FIRMWARE ACCESS:
/// - EC I/O Ports: 0x62 (data), 0x66 (command/status)
/// - ACPI: _WMI interface for Lenovo settings
/// - SMBus: Battery and sensor communication
/// </summary>
public class LegionSlim7iGen9Profile
{
    // ==================== EC (Embedded Controller) Register Map ====================
    // ITE IT5570E Embedded Controller

    // EC I/O Ports
    public const ushort EC_DATA_PORT = 0x62;
    public const ushort EC_CMD_STATUS_PORT = 0x66;

    // EC Commands
    public const byte EC_CMD_READ = 0x80;
    public const byte EC_CMD_WRITE = 0x81;
    public const byte EC_CMD_BURST_ENABLE = 0x82;
    public const byte EC_CMD_BURST_DISABLE = 0x83;
    public const byte EC_CMD_QUERY = 0x84;

    // EC Status Flags
    public const byte EC_STATUS_OBF = 0x01;  // Output Buffer Full
    public const byte EC_STATUS_IBF = 0x02;  // Input Buffer Full
    public const byte EC_STATUS_BURST = 0x10; // Burst Mode Active
    public const byte EC_STATUS_SCI = 0x20;  // SCI Event Pending

    // ==================== Temperature Sensors ====================
    public const byte EC_TEMP_CPU = 0xC0;           // CPU Temperature (°C)
    public const byte EC_TEMP_GPU = 0xC1;           // GPU Temperature (°C)
    public const byte EC_TEMP_SYSTEM = 0xC2;        // System/Motherboard Temperature
    public const byte EC_TEMP_VRM_CPU = 0xC3;       // CPU VRM Temperature
    public const byte EC_TEMP_VRM_GPU = 0xC4;       // GPU VRM Temperature
    public const byte EC_TEMP_BATTERY = 0xC5;       // Battery Temperature
    public const byte EC_TEMP_NVME_1 = 0xC6;        // NVMe SSD 1 Temperature
    public const byte EC_TEMP_NVME_2 = 0xC7;        // NVMe SSD 2 Temperature (if present)
    public const byte EC_TEMP_AMBIENT = 0xC8;       // Ambient Temperature

    // ==================== Fan Control ====================
    public const byte EC_FAN_CPU_SPEED_LSB = 0xD0;  // CPU Fan Speed (RPM) Low Byte
    public const byte EC_FAN_CPU_SPEED_MSB = 0xD1;  // CPU Fan Speed (RPM) High Byte
    public const byte EC_FAN_GPU_SPEED_LSB = 0xD2;  // GPU Fan Speed (RPM) Low Byte
    public const byte EC_FAN_GPU_SPEED_MSB = 0xD3;  // GPU Fan Speed (RPM) High Byte

    public const byte EC_FAN_CPU_PWM = 0xD4;        // CPU Fan PWM Duty Cycle (0-255)
    public const byte EC_FAN_GPU_PWM = 0xD5;        // GPU Fan PWM Duty Cycle (0-255)

    public const byte EC_FAN_MODE = 0xD6;           // Fan Mode
    public const byte FAN_MODE_AUTO = 0x00;         // Automatic fan control
    public const byte FAN_MODE_MANUAL = 0x01;       // Manual fan control
    public const byte FAN_MODE_FULL_SPEED = 0x02;   // Maximum speed

    public const byte EC_FAN_CURVE_BASE = 0xE0;     // Fan curve data start (10 temp + 10 speed points)
    // EC_FAN_CURVE_BASE + 0-9: Temperature points (°C)
    // EC_FAN_CURVE_BASE + 10-19: Speed points (PWM 0-255)

    // ==================== Power Management ====================
    public const byte EC_POWER_MODE = 0xA0;         // Current power mode
    public const byte POWER_MODE_QUIET = 0x01;      // Quiet mode (minimal fans)
    public const byte POWER_MODE_BALANCED = 0x02;   // Balanced mode
    public const byte POWER_MODE_PERFORMANCE = 0x03; // Performance mode
    public const byte POWER_MODE_CUSTOM = 0xFF;     // Custom/AI mode

    public const byte EC_BATTERY_CONSERVATION = 0xA1; // Battery conservation mode
    public const byte EC_RAPID_CHARGE = 0xA2;       // Rapid charging enable

    public const byte EC_CPU_POWER_LIMIT_LSB = 0xA3; // CPU Power Limit (W) Low Byte
    public const byte EC_CPU_POWER_LIMIT_MSB = 0xA4; // CPU Power Limit (W) High Byte
    public const byte EC_GPU_POWER_LIMIT_LSB = 0xA5; // GPU Power Limit (W) Low Byte
    public const byte EC_GPU_POWER_LIMIT_MSB = 0xA6; // GPU Power Limit (W) High Byte

    // ==================== Battery Information ====================
    public const byte EC_BATTERY_VOLTAGE_LSB = 0xB0; // Battery Voltage (mV) Low Byte
    public const byte EC_BATTERY_VOLTAGE_MSB = 0xB1; // Battery Voltage (mV) High Byte
    public const byte EC_BATTERY_CURRENT_LSB = 0xB2; // Battery Current (mA) Low Byte (signed)
    public const byte EC_BATTERY_CURRENT_MSB = 0xB3; // Battery Current (mA) High Byte (signed)
    public const byte EC_BATTERY_CAPACITY = 0xB4;    // Battery Remaining Capacity (%)
    public const byte EC_BATTERY_STATUS = 0xB5;      // Battery Status Flags

    public const byte BATTERY_STATUS_CHARGING = 0x01;
    public const byte BATTERY_STATUS_DISCHARGING = 0x02;
    public const byte BATTERY_STATUS_FULL = 0x04;
    public const byte BATTERY_STATUS_CRITICAL = 0x08;

    // ==================== Keyboard & Lighting ====================
    public const byte EC_KB_BACKLIGHT_LEVEL = 0xF0; // Keyboard backlight level (0-4)
    public const byte EC_KB_RGB_MODE = 0xF1;        // RGB keyboard mode
    public const byte EC_KB_RGB_BRIGHTNESS = 0xF2;  // RGB brightness (0-255)
    public const byte EC_LOGO_BACKLIGHT = 0xF3;     // Legion logo backlight

    // ==================== Display & GPU ====================
    public const byte EC_DISPLAY_OVERDRIVE = 0xF4;  // Display OverDrive enable
    public const byte EC_DISPLAY_GSYNC = 0xF5;      // G-Sync enable
    public const byte EC_GPU_HYBRID_MODE = 0xF6;    // Hybrid mode (iGPU/dGPU)
    public const byte GPU_MODE_HYBRID = 0x00;       // Hybrid (switchable)
    public const byte GPU_MODE_DISCRETE_ONLY = 0x01; // dGPU only
    public const byte GPU_MODE_IGPU_ONLY = 0x02;    // iGPU only (not supported on all models)

    // ==================== Hardware Specifications ====================

    public static readonly HardwareSpec Hardware = new()
    {
        ModelName = "Legion Slim 7i Gen 9",
        ModelYear = 2024,
        Generation = 9,

        // CPU Specifications
        CpuSocketType = "BGA (Soldered)",
        CpuTdpMin = 28,
        CpuTdpBase = 45,
        CpuTdpMax = 115,
        CpuCoreCount = 16, // 6P + 8E + 2LP (Ultra 9 185H)
        CpuThreadCount = 22,
        CpuBaseFrequency = 2300, // MHz
        CpuBoostFrequency = 5100, // MHz

        // GPU Specifications
        GpuModel = "NVIDIA RTX 4070 Laptop",
        GpuTgpMin = 90,
        GpuTgpBase = 115,
        GpuTgpMax = 140,
        GpuVramGb = 8,
        GpuVramType = "GDDR6",

        // Memory Specifications
        RamType = "LPDDR5X",
        RamSpeedMhz = 7467,
        RamMaxGb = 32,
        RamSlots = 0, // Soldered

        // Display Specifications
        DisplaySizeInch = 16.0,
        DisplayResolutionWidth = 3200,
        DisplayResolutionHeight = 2000,
        DisplayRefreshRateMax = 165, // or 240Hz variant
        DisplayPanelType = "IPS",
        DisplayColorGamut = "100% sRGB",

        // Battery Specifications
        BatteryCapacityWh = 99.9,
        BatteryCells = 4,
        BatteryVoltageNominal = 15.36,

        // Cooling Specifications
        FanCount = 2,
        HeatPipeCount = 3,
        VaporChamber = true,
        MaxFanSpeedRpm = 5500,

        // Thermal Limits
        CpuTjMax = 100, // °C
        GpuTjMax = 87,  // °C
        VrmTempWarning = 85, // °C
        VrmTempCritical = 95, // °C
    };

    // ==================== Power Profiles ====================

    public static readonly Dictionary<string, PowerProfile> Profiles = new()
    {
        ["Quiet"] = new PowerProfile
        {
            Name = "Quiet",
            CpuPowerLimitPl1 = 28,
            CpuPowerLimitPl2 = 45,
            GpuPowerLimit = 90,
            FanCurvePreset = FanCurvePreset.Quiet,
            TargetNoiseLevelDba = 25
        },

        ["Balanced"] = new PowerProfile
        {
            Name = "Balanced",
            CpuPowerLimitPl1 = 45,
            CpuPowerLimitPl2 = 65,
            GpuPowerLimit = 115,
            FanCurvePreset = FanCurvePreset.Balanced,
            TargetNoiseLevelDba = 35
        },

        ["Performance"] = new PowerProfile
        {
            Name = "Performance",
            CpuPowerLimitPl1 = 65,
            CpuPowerLimitPl2 = 115,
            GpuPowerLimit = 140,
            FanCurvePreset = FanCurvePreset.Performance,
            TargetNoiseLevelDba = 45
        },

        ["MediaPlayback"] = new PowerProfile
        {
            Name = "Media Playback (AI Optimized)",
            CpuPowerLimitPl1 = 20,
            CpuPowerLimitPl2 = 25,
            GpuPowerLimit = 0, // iGPU only
            FanCurvePreset = FanCurvePreset.Silent,
            TargetNoiseLevelDba = 20
        },

        ["Gaming"] = new PowerProfile
        {
            Name = "Gaming (AI Optimized)",
            CpuPowerLimitPl1 = 55,
            CpuPowerLimitPl2 = 85,
            GpuPowerLimit = 140,
            FanCurvePreset = FanCurvePreset.Aggressive,
            TargetNoiseLevelDba = 50
        }
    };

    // ==================== Fan Curve Presets ====================

    public static readonly Dictionary<FanCurvePreset, FanCurve> FanCurves = new()
    {
        [FanCurvePreset.Silent] = new FanCurve
        {
            Name = "Silent",
            TemperaturePoints = new[] { 30, 40, 50, 60, 65, 70, 75, 80, 85, 90 },
            SpeedPercent = new[] { 0, 15, 25, 30, 35, 40, 50, 65, 80, 100 }
        },

        [FanCurvePreset.Quiet] = new FanCurve
        {
            Name = "Quiet",
            TemperaturePoints = new[] { 30, 40, 50, 60, 65, 70, 75, 80, 85, 90 },
            SpeedPercent = new[] { 20, 25, 30, 35, 40, 50, 60, 75, 90, 100 }
        },

        [FanCurvePreset.Balanced] = new FanCurve
        {
            Name = "Balanced",
            TemperaturePoints = new[] { 30, 40, 50, 60, 65, 70, 75, 80, 85, 90 },
            SpeedPercent = new[] { 25, 30, 40, 50, 55, 65, 75, 85, 95, 100 }
        },

        [FanCurvePreset.Performance] = new FanCurve
        {
            Name = "Performance",
            TemperaturePoints = new[] { 30, 40, 50, 60, 65, 70, 75, 80, 85, 90 },
            SpeedPercent = new[] { 30, 40, 50, 60, 70, 80, 85, 90, 95, 100 }
        },

        [FanCurvePreset.Aggressive] = new FanCurve
        {
            Name = "Aggressive",
            TemperaturePoints = new[] { 30, 40, 50, 60, 65, 70, 75, 80, 85, 90 },
            SpeedPercent = new[] { 40, 50, 60, 70, 75, 80, 85, 90, 95, 100 }
        }
    };
}

// ==================== Supporting Data Structures ====================

public class HardwareSpec
{
    public string ModelName { get; set; } = "";
    public int ModelYear { get; set; }
    public int Generation { get; set; }

    // CPU
    public string CpuSocketType { get; set; } = "";
    public int CpuTdpMin { get; set; }
    public int CpuTdpBase { get; set; }
    public int CpuTdpMax { get; set; }
    public int CpuCoreCount { get; set; }
    public int CpuThreadCount { get; set; }
    public int CpuBaseFrequency { get; set; }
    public int CpuBoostFrequency { get; set; }

    // GPU
    public string GpuModel { get; set; } = "";
    public int GpuTgpMin { get; set; }
    public int GpuTgpBase { get; set; }
    public int GpuTgpMax { get; set; }
    public int GpuVramGb { get; set; }
    public string GpuVramType { get; set; } = "";

    // RAM
    public string RamType { get; set; } = "";
    public int RamSpeedMhz { get; set; }
    public int RamMaxGb { get; set; }
    public int RamSlots { get; set; }

    // Display
    public double DisplaySizeInch { get; set; }
    public int DisplayResolutionWidth { get; set; }
    public int DisplayResolutionHeight { get; set; }
    public int DisplayRefreshRateMax { get; set; }
    public string DisplayPanelType { get; set; } = "";
    public string DisplayColorGamut { get; set; } = "";

    // Battery
    public double BatteryCapacityWh { get; set; }
    public int BatteryCells { get; set; }
    public double BatteryVoltageNominal { get; set; }

    // Cooling
    public int FanCount { get; set; }
    public int HeatPipeCount { get; set; }
    public bool VaporChamber { get; set; }
    public int MaxFanSpeedRpm { get; set; }

    // Thermal Limits
    public int CpuTjMax { get; set; }
    public int GpuTjMax { get; set; }
    public int VrmTempWarning { get; set; }
    public int VrmTempCritical { get; set; }
}

public class PowerProfile
{
    public string Name { get; set; } = "";
    public int CpuPowerLimitPl1 { get; set; }
    public int CpuPowerLimitPl2 { get; set; }
    public int GpuPowerLimit { get; set; }
    public FanCurvePreset FanCurvePreset { get; set; }
    public int TargetNoiseLevelDba { get; set; }
}

public class FanCurve
{
    public string Name { get; set; } = "";
    public int[] TemperaturePoints { get; set; } = Array.Empty<int>();
    public int[] SpeedPercent { get; set; } = Array.Empty<int>();

    public int GetFanSpeed(int temperature)
    {
        if (TemperaturePoints.Length != SpeedPercent.Length)
            return 50; // Default 50%

        // Find position in curve
        for (int i = 0; i < TemperaturePoints.Length - 1; i++)
        {
            if (temperature >= TemperaturePoints[i] && temperature < TemperaturePoints[i + 1])
            {
                // Linear interpolation
                double ratio = (double)(temperature - TemperaturePoints[i]) /
                             (TemperaturePoints[i + 1] - TemperaturePoints[i]);
                return SpeedPercent[i] + (int)(ratio * (SpeedPercent[i + 1] - SpeedPercent[i]));
            }
        }

        // Above highest point - use max speed
        if (temperature >= TemperaturePoints[^1])
            return SpeedPercent[^1];

        // Below lowest point - use min speed
        return SpeedPercent[0];
    }
}

public enum FanCurvePreset
{
    Silent,
    Quiet,
    Balanced,
    Performance,
    Aggressive
}
