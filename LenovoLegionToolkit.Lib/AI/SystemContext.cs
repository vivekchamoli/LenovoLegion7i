using System;
using System.Collections.Generic;
using System.Diagnostics;
using LenovoLegionToolkit.Lib.Controllers;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Unified system context shared across all optimization agents
/// Gathered once per optimization cycle to ensure consistent decision-making
/// </summary>
public class SystemContext
{
    public required ThermalState ThermalState { get; set; }
    public required PowerState PowerState { get; set; }
    public required GpuSystemState GpuState { get; set; }
    public required BatteryState BatteryState { get; set; }
    public WorkloadProfile CurrentWorkload { get; set; } = new();
    public UserIntent UserIntent { get; set; }
    public DateTime Timestamp { get; set; }
    public TimeSpan UpTime { get; set; }

    /// <summary>
    /// Additional context data for agent-specific needs
    /// </summary>
    public Dictionary<string, object> ExtendedData { get; set; } = new();
}

/// <summary>
/// Thermal state snapshot from Gen 9 sensors
/// </summary>
public class ThermalState
{
    public byte CpuTemp { get; set; }
    public byte GpuTemp { get; set; }
    public byte GpuHotspot { get; set; }
    public byte GpuMemoryTemp { get; set; }
    public byte VrmTemp { get; set; }
    public byte SsdTemp { get; set; }
    public byte RamTemp { get; set; }
    public byte BatteryTemp { get; set; }
    public int Fan1Speed { get; set; }
    public int Fan2Speed { get; set; }
    public byte AmbientTemp { get; set; } = 25; // Estimated
    public required ThermalTrend Trend { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Thermal trend analysis
/// </summary>
public class ThermalTrend
{
    public double CpuTrendPerSecond { get; set; }
    public double GpuTrendPerSecond { get; set; }
    public bool IsRisingRapidly { get; set; }
    public bool IsStable { get; set; }
    public bool IsCooling { get; set; }
}

/// <summary>
/// Power state from system
/// </summary>
public class PowerState
{
    public PowerModeState CurrentPowerMode { get; set; }
    public int CurrentPL1 { get; set; }
    public int CurrentPL2 { get; set; }
    public int CurrentPL4 { get; set; }
    public int GpuTGP { get; set; }
    public int TotalSystemPower { get; set; }
    public bool IsACConnected { get; set; }
    public FanProfile CurrentFanProfile { get; set; }
}

/// <summary>
/// GPU state details (renamed from GpuState to avoid collision with GPUState enum)
/// </summary>
public class GpuSystemState
{
    public GPUState State { get; set; }
    public string? PerformanceState { get; set; }
    public List<Process> ActiveProcesses { get; set; } = new();
    public int GpuUtilizationPercent { get; set; }
    public int MemoryUtilizationPercent { get; set; }
    public int CoreClockMHz { get; set; }
    public int MemoryClockMHz { get; set; }
}

/// <summary>
/// Battery state
/// </summary>
public class BatteryState
{
    public bool IsOnBattery { get; set; }
    public int ChargePercent { get; set; }
    public int ChargeRateMw { get; set; } // Positive = charging, negative = discharging
    public TimeSpan EstimatedTimeRemaining { get; set; }
    public int DesignCapacityMwh { get; set; }
    public int FullChargeCapacityMwh { get; set; }
    public int BatteryHealth { get; set; } // Percentage
    public BatteryChargingMode ChargingMode { get; set; }
}

/// <summary>
/// Battery charging mode
/// </summary>
public enum BatteryChargingMode
{
    Standard,
    RapidCharge,
    Conservation,
    Custom
}

/// <summary>
/// Workload profile classification
/// </summary>
public class WorkloadProfile
{
    public WorkloadType Type { get; set; }
    public int CpuUtilizationPercent { get; set; }
    public int GpuUtilizationPercent { get; set; }
    public List<string> ActiveApplications { get; set; } = new();
    public List<string> GamingProcesses { get; set; } = new();
    public bool IsUserActive { get; set; } // Mouse/keyboard activity
    public TimeSpan TimeInCurrentWorkload { get; set; }
    public double Confidence { get; set; } // ML classification confidence
}

/// <summary>
/// Workload type classification
/// </summary>
public enum WorkloadType
{
    Idle,
    LightProductivity,
    HeavyProductivity,
    Gaming,
    AIWorkload,
    ContentCreation,
    Mixed,
    Unknown
}

/// <summary>
/// User intent for system behavior
/// </summary>
public enum UserIntent
{
    Balanced,           // Default intelligent behavior
    MaxPerformance,     // Prioritize performance over everything
    BatterySaving,      // Maximize battery life
    Quiet,              // Minimize noise (fan speeds)
    Gaming,             // Gaming-optimized profile
    Productivity,       // Sustained performance for work
    Custom              // User-defined behavior
}

/// <summary>
/// Execution plan after arbitration
/// </summary>
public class ExecutionPlan
{
    public List<ResourceAction> Actions { get; set; } = new();
    public List<Conflict> Conflicts { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}
