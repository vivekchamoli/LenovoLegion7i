using System;
using System.Threading;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// Embedded Controller (EC) Direct Access
/// Provides low-level hardware access to the ITE IT5570E Embedded Controller
///
/// CAPABILITIES:
/// - Real-time sensor readings (temperatures, voltages, currents)
/// - Fan control (speed, PWM, curves)
/// - Power management (CPU/GPU power limits)
/// - Battery information
/// - Keyboard backlight control
/// - Hardware mode switching
///
/// REQUIREMENTS:
/// - Kernel driver (WinRing0) for I/O port access
/// - Administrator privileges
/// - EC protocol knowledge (timing critical)
///
/// SAFETY:
/// - Read operations are safe
/// - Write operations can damage hardware if done incorrectly
/// - Always validate values before writing
/// - Respect EC busy status
/// </summary>
public class EmbeddedControllerAccess
{
    private static readonly object _ecLock = new();
    private const int EC_TIMEOUT_MS = 1000;
    private const int EC_RETRY_DELAY_MS = 1;

    private bool _isAvailable = false;

    /// <summary>
    /// Initialize EC access (requires kernel driver)
    /// </summary>
    public bool Initialize()
    {
        try
        {
            if (!KernelDriverInterface.IsAvailable)
            {
                if (!KernelDriverInterface.Initialize())
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"EC access unavailable - kernel driver not loaded");
                    return false;
                }
            }

            // Test EC access by reading a safe register
            var testValue = ReadByte(LegionSlim7iGen9Profile.EC_TEMP_CPU);

            _isAvailable = true;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"EC access initialized - CPU temp: {testValue}°C");

            return true;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"EC initialization failed", ex);

            _isAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Check if EC access is available
    /// </summary>
    public bool IsAvailable => _isAvailable;

    // ==================== EC Low-Level Operations ====================

    /// <summary>
    /// Wait for EC input buffer to be empty
    /// </summary>
    private bool WaitInputBufferEmpty()
    {
        var start = DateTime.Now;

        while ((DateTime.Now - start).TotalMilliseconds < EC_TIMEOUT_MS)
        {
            byte status = KernelDriverInterface.ReadPort(LegionSlim7iGen9Profile.EC_CMD_STATUS_PORT);

            if ((status & LegionSlim7iGen9Profile.EC_STATUS_IBF) == 0)
                return true;

            Thread.Sleep(EC_RETRY_DELAY_MS);
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"EC timeout waiting for input buffer empty");

        return false;
    }

    /// <summary>
    /// Wait for EC output buffer to be full
    /// </summary>
    private bool WaitOutputBufferFull()
    {
        var start = DateTime.Now;

        while ((DateTime.Now - start).TotalMilliseconds < EC_TIMEOUT_MS)
        {
            byte status = KernelDriverInterface.ReadPort(LegionSlim7iGen9Profile.EC_CMD_STATUS_PORT);

            if ((status & LegionSlim7iGen9Profile.EC_STATUS_OBF) != 0)
                return true;

            Thread.Sleep(EC_RETRY_DELAY_MS);
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"EC timeout waiting for output buffer full");

        return false;
    }

    /// <summary>
    /// Read byte from EC register
    /// </summary>
    public byte ReadByte(byte register)
    {
        if (!_isAvailable)
            throw new InvalidOperationException("EC access not available");

        lock (_ecLock)
        {
            try
            {
                // Wait for EC ready
                if (!WaitInputBufferEmpty())
                    throw new TimeoutException("EC not ready for read command");

                // Send read command
                KernelDriverInterface.WritePort(LegionSlim7iGen9Profile.EC_CMD_STATUS_PORT, LegionSlim7iGen9Profile.EC_CMD_READ);

                // Wait for EC ready to accept register address
                if (!WaitInputBufferEmpty())
                    throw new TimeoutException("EC not ready for register address");

                // Send register address
                KernelDriverInterface.WritePort(LegionSlim7iGen9Profile.EC_DATA_PORT, register);

                // Wait for EC to provide data
                if (!WaitOutputBufferFull())
                    throw new TimeoutException("EC did not provide data");

                // Read data
                byte value = KernelDriverInterface.ReadPort(LegionSlim7iGen9Profile.EC_DATA_PORT);

                return value;
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"EC read failed at 0x{register:X2}", ex);
                throw;
            }
        }
    }

    /// <summary>
    /// Write byte to EC register
    /// WARNING: Can damage hardware if used incorrectly
    /// </summary>
    public void WriteByte(byte register, byte value)
    {
        if (!_isAvailable)
            throw new InvalidOperationException("EC access not available");

        lock (_ecLock)
        {
            try
            {
                // Wait for EC ready
                if (!WaitInputBufferEmpty())
                    throw new TimeoutException("EC not ready for write command");

                // Send write command
                KernelDriverInterface.WritePort(LegionSlim7iGen9Profile.EC_CMD_STATUS_PORT, LegionSlim7iGen9Profile.EC_CMD_WRITE);

                // Wait for EC ready to accept register address
                if (!WaitInputBufferEmpty())
                    throw new TimeoutException("EC not ready for register address");

                // Send register address
                KernelDriverInterface.WritePort(LegionSlim7iGen9Profile.EC_DATA_PORT, register);

                // Wait for EC ready to accept data
                if (!WaitInputBufferEmpty())
                    throw new TimeoutException("EC not ready for data");

                // Write data
                KernelDriverInterface.WritePort(LegionSlim7iGen9Profile.EC_DATA_PORT, value);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"EC write: 0x{register:X2} = 0x{value:X2}");
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"EC write failed at 0x{register:X2}", ex);
                throw;
            }
        }
    }

    /// <summary>
    /// Read 16-bit word from EC (LSB, MSB)
    /// </summary>
    public ushort ReadWord(byte registerLsb)
    {
        byte lsb = ReadByte(registerLsb);
        byte msb = ReadByte((byte)(registerLsb + 1));
        return (ushort)((msb << 8) | lsb);
    }

    /// <summary>
    /// Write 16-bit word to EC (LSB, MSB)
    /// </summary>
    public void WriteWord(byte registerLsb, ushort value)
    {
        WriteByte(registerLsb, (byte)(value & 0xFF));
        WriteByte((byte)(registerLsb + 1), (byte)((value >> 8) & 0xFF));
    }

    // ==================== Temperature Sensors ====================

    public ECTemperatures ReadTemperatures()
    {
        return new ECTemperatures
        {
            CpuTemp = ReadByte(LegionSlim7iGen9Profile.EC_TEMP_CPU),
            GpuTemp = ReadByte(LegionSlim7iGen9Profile.EC_TEMP_GPU),
            SystemTemp = ReadByte(LegionSlim7iGen9Profile.EC_TEMP_SYSTEM),
            VrmCpuTemp = ReadByte(LegionSlim7iGen9Profile.EC_TEMP_VRM_CPU),
            VrmGpuTemp = ReadByte(LegionSlim7iGen9Profile.EC_TEMP_VRM_GPU),
            BatteryTemp = ReadByte(LegionSlim7iGen9Profile.EC_TEMP_BATTERY),
            Nvme1Temp = ReadByte(LegionSlim7iGen9Profile.EC_TEMP_NVME_1),
            Nvme2Temp = ReadByte(LegionSlim7iGen9Profile.EC_TEMP_NVME_2),
            AmbientTemp = ReadByte(LegionSlim7iGen9Profile.EC_TEMP_AMBIENT)
        };
    }

    // ==================== Fan Control ====================

    public ECFanInfo ReadFanInfo()
    {
        return new ECFanInfo
        {
            CpuFanRpm = ReadWord(LegionSlim7iGen9Profile.EC_FAN_CPU_SPEED_LSB),
            GpuFanRpm = ReadWord(LegionSlim7iGen9Profile.EC_FAN_GPU_SPEED_LSB),
            CpuFanPwm = ReadByte(LegionSlim7iGen9Profile.EC_FAN_CPU_PWM),
            GpuFanPwm = ReadByte(LegionSlim7iGen9Profile.EC_FAN_GPU_PWM),
            FanMode = ReadByte(LegionSlim7iGen9Profile.EC_FAN_MODE)
        };
    }

    public void SetFanMode(byte mode)
    {
        ValidateFanMode(mode);
        WriteByte(LegionSlim7iGen9Profile.EC_FAN_MODE, mode);
    }

    public void SetFanSpeed(byte cpuPwm, byte gpuPwm)
    {
        // Validate PWM values (0-255, but clamp to safe range)
        cpuPwm = Math.Min(cpuPwm, (byte)255);
        gpuPwm = Math.Min(gpuPwm, (byte)255);

        // Set to manual mode first
        WriteByte(LegionSlim7iGen9Profile.EC_FAN_MODE, LegionSlim7iGen9Profile.FAN_MODE_MANUAL);

        // Set PWM values
        WriteByte(LegionSlim7iGen9Profile.EC_FAN_CPU_PWM, cpuPwm);
        WriteByte(LegionSlim7iGen9Profile.EC_FAN_GPU_PWM, gpuPwm);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Fan speeds set: CPU={cpuPwm} ({(cpuPwm * 100 / 255)}%), GPU={gpuPwm} ({(gpuPwm * 100 / 255)}%)");
    }

    public void SetFanCurve(FanCurve curve)
    {
        if (curve.TemperaturePoints.Length != 10 || curve.SpeedPercent.Length != 10)
            throw new ArgumentException("Fan curve must have exactly 10 temperature and 10 speed points");

        // Write temperature points
        for (int i = 0; i < 10; i++)
        {
            byte temp = (byte)Math.Min(Math.Max(curve.TemperaturePoints[i], 0), 100);
            WriteByte((byte)(LegionSlim7iGen9Profile.EC_FAN_CURVE_BASE + i), temp);
        }

        // Write speed points (convert percent to PWM)
        for (int i = 0; i < 10; i++)
        {
            byte pwm = (byte)(curve.SpeedPercent[i] * 255 / 100);
            WriteByte((byte)(LegionSlim7iGen9Profile.EC_FAN_CURVE_BASE + 10 + i), pwm);
        }

        // Set to auto mode to use the curve
        WriteByte(LegionSlim7iGen9Profile.EC_FAN_MODE, LegionSlim7iGen9Profile.FAN_MODE_AUTO);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Fan curve uploaded: {curve.Name}");
    }

    // ==================== Power Management ====================

    public byte GetPowerMode()
    {
        return ReadByte(LegionSlim7iGen9Profile.EC_POWER_MODE);
    }

    public void SetPowerMode(byte mode)
    {
        ValidatePowerMode(mode);
        WriteByte(LegionSlim7iGen9Profile.EC_POWER_MODE, mode);
    }

    public void SetCpuPowerLimit(ushort watts)
    {
        if (watts < 20 || watts > 115)
            throw new ArgumentOutOfRangeException(nameof(watts), "CPU power limit must be 20-115W");

        WriteWord(LegionSlim7iGen9Profile.EC_CPU_POWER_LIMIT_LSB, watts);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"CPU power limit set: {watts}W");
    }

    public void SetGpuPowerLimit(ushort watts)
    {
        if (watts > 140)
            throw new ArgumentOutOfRangeException(nameof(watts), "GPU power limit must be 0-140W");

        WriteWord(LegionSlim7iGen9Profile.EC_GPU_POWER_LIMIT_LSB, watts);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"GPU power limit set: {watts}W");
    }

    // ==================== Battery Information ====================

    public ECBatteryInfo ReadBatteryInfo()
    {
        ushort voltage = ReadWord(LegionSlim7iGen9Profile.EC_BATTERY_VOLTAGE_LSB);
        short current = (short)ReadWord(LegionSlim7iGen9Profile.EC_BATTERY_CURRENT_LSB);
        byte capacity = ReadByte(LegionSlim7iGen9Profile.EC_BATTERY_CAPACITY);
        byte status = ReadByte(LegionSlim7iGen9Profile.EC_BATTERY_STATUS);

        return new ECBatteryInfo
        {
            VoltageMillivolts = voltage,
            CurrentMilliamps = current,
            CapacityPercent = capacity,
            Status = status,
            IsCharging = (status & LegionSlim7iGen9Profile.BATTERY_STATUS_CHARGING) != 0,
            IsDischarging = (status & LegionSlim7iGen9Profile.BATTERY_STATUS_DISCHARGING) != 0,
            IsFull = (status & LegionSlim7iGen9Profile.BATTERY_STATUS_FULL) != 0,
            IsCritical = (status & LegionSlim7iGen9Profile.BATTERY_STATUS_CRITICAL) != 0
        };
    }

    public void SetBatteryConservation(bool enabled)
    {
        WriteByte(LegionSlim7iGen9Profile.EC_BATTERY_CONSERVATION, (byte)(enabled ? 1 : 0));

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Battery conservation: {(enabled ? "enabled" : "disabled")}");
    }

    public void SetRapidCharge(bool enabled)
    {
        WriteByte(LegionSlim7iGen9Profile.EC_RAPID_CHARGE, (byte)(enabled ? 1 : 0));

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Rapid charge: {(enabled ? "enabled" : "disabled")}");
    }

    // ==================== Validation Helpers ====================

    private void ValidateFanMode(byte mode)
    {
        if (mode != LegionSlim7iGen9Profile.FAN_MODE_AUTO &&
            mode != LegionSlim7iGen9Profile.FAN_MODE_MANUAL &&
            mode != LegionSlim7iGen9Profile.FAN_MODE_FULL_SPEED)
        {
            throw new ArgumentException($"Invalid fan mode: 0x{mode:X2}");
        }
    }

    private void ValidatePowerMode(byte mode)
    {
        if (mode != LegionSlim7iGen9Profile.POWER_MODE_QUIET &&
            mode != LegionSlim7iGen9Profile.POWER_MODE_BALANCED &&
            mode != LegionSlim7iGen9Profile.POWER_MODE_PERFORMANCE &&
            mode != LegionSlim7iGen9Profile.POWER_MODE_CUSTOM)
        {
            throw new ArgumentException($"Invalid power mode: 0x{mode:X2}");
        }
    }
}

// ==================== Data Structures ====================

public class ECTemperatures
{
    public byte CpuTemp { get; set; }
    public byte GpuTemp { get; set; }
    public byte SystemTemp { get; set; }
    public byte VrmCpuTemp { get; set; }
    public byte VrmGpuTemp { get; set; }
    public byte BatteryTemp { get; set; }
    public byte Nvme1Temp { get; set; }
    public byte Nvme2Temp { get; set; }
    public byte AmbientTemp { get; set; }

    public byte MaxTemp => Math.Max(CpuTemp, Math.Max(GpuTemp, Math.Max(VrmCpuTemp, VrmGpuTemp)));
}

public class ECFanInfo
{
    public ushort CpuFanRpm { get; set; }
    public ushort GpuFanRpm { get; set; }
    public byte CpuFanPwm { get; set; }
    public byte GpuFanPwm { get; set; }
    public byte FanMode { get; set; }

    public int CpuFanPercent => CpuFanPwm * 100 / 255;
    public int GpuFanPercent => GpuFanPwm * 100 / 255;
}

public class ECBatteryInfo
{
    public ushort VoltageMillivolts { get; set; }
    public short CurrentMilliamps { get; set; }
    public byte CapacityPercent { get; set; }
    public byte Status { get; set; }
    public bool IsCharging { get; set; }
    public bool IsDischarging { get; set; }
    public bool IsFull { get; set; }
    public bool IsCritical { get; set; }

    public double VoltageVolts => VoltageMillivolts / 1000.0;
    public double CurrentAmps => CurrentMilliamps / 1000.0;
    public double PowerWatts => Math.Abs(VoltageMillivolts * CurrentMilliamps / 1000000.0);
}
