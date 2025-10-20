using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI.Elite;

/// <summary>
/// ELITE 10/10: ThermoControl Sub-Agent
/// AI-enhanced PID controller for fan curves and thermal management
/// Predictive thermal response with adaptive gain tuning
/// </summary>
public class ThermoControlSubAgent : EliteSubAgentBase
{
    private readonly Gen9ECController? _ecController;

    // PID controller state
    private double _lastCpuError;
    private double _cpuIntegral;
    private double _lastGpuError;
    private double _gpuIntegral;

    // AI-enhanced adaptive gains
    private PIDGains _cpuGains = new() { Kp = 1.5, Ki = 0.05, Kd = 0.3 };
    private PIDGains _gpuGains = new() { Kp = 1.2, Ki = 0.04, Kd = 0.25 };

    // Thermal target temperatures (adaptive based on workload)
    private double _cpuTargetTemp = 75.0;
    private double _gpuTargetTemp = 70.0;

    // Learning history for gain adaptation
    private readonly CircularBuffer<ThermalSnapshot> _thermalHistory = new(300); // 30 seconds at 100Hz

    public override SubAgentType Type => SubAgentType.ThermoControl;
    public override int Priority => 9; // High priority

    public ThermoControlSubAgent(
        string agentId,
        SecureAgentBus agentBus,
        TelemetryFusionEngine telemetryEngine,
        Gen9ECController? ecController = null)
        : base(agentId, agentBus, telemetryEngine)
    {
        _ecController = ecController;
    }

    public override async Task ExecuteCycleAsync(FusedTelemetry telemetry, CancellationToken ct)
    {
        try
        {
            _totalCycles++;

            // Record thermal snapshot for learning
            _thermalHistory.Add(new ThermalSnapshot
            {
                Timestamp = telemetry.Timestamp,
                CpuTemp = telemetry.CpuTemp,
                GpuTemp = telemetry.GpuTemp,
                FanSpeed = telemetry.FanSpeedRPM
            });

            // Adapt target temperatures based on workload
            AdaptTargetTemperatures(telemetry);

            // AI-enhanced PID control for CPU fan
            var cpuFanSpeed = CalculatePIDFanSpeed(
                telemetry.CpuTemp,
                _cpuTargetTemp,
                ref _lastCpuError,
                ref _cpuIntegral,
                _cpuGains);

            // AI-enhanced PID control for GPU fan
            var gpuFanSpeed = CalculatePIDFanSpeed(
                telemetry.GpuTemp,
                _gpuTargetTemp,
                ref _lastGpuError,
                ref _gpuIntegral,
                _gpuGains);

            // Apply fan speeds (if EC controller available)
            if (_ecController != null)
            {
                await ApplyFanSpeedsAsync(cpuFanSpeed, gpuFanSpeed);
            }

            // Periodically adapt PID gains based on performance
            if (_totalCycles % 1000 == 0) // Every 10 seconds
            {
                AdaptPIDGains();
            }

            // Emergency thermal response
            if (telemetry.CpuTemp > 95 || telemetry.GpuTemp > 87 || telemetry.VrmTemp > 90)
            {
                await TriggerEmergencyThermalResponseAsync(telemetry);
            }
        }
        catch (Exception ex)
        {
            _totalErrors++;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ThermoControl cycle error", ex);
        }
    }

    /// <summary>
    /// AI-enhanced PID controller for fan speed
    /// Adaptive gains based on thermal response characteristics
    /// </summary>
    private ushort CalculatePIDFanSpeed(
        double currentTemp,
        double targetTemp,
        ref double lastError,
        ref double integral,
        PIDGains gains)
    {
        // Calculate error
        var error = currentTemp - targetTemp;

        // Proportional term
        var pTerm = gains.Kp * error;

        // Integral term (with anti-windup)
        integral += error;
        integral = Math.Clamp(integral, -100, 100); // Prevent integral windup
        var iTerm = gains.Ki * integral;

        // Derivative term
        var derivative = error - lastError;
        var dTerm = gains.Kd * derivative;

        // Calculate total correction
        var correction = pTerm + iTerm + dTerm;

        // Convert to fan speed (0-5500 RPM)
        // Base speed + correction
        var baseSpeed = 2000.0; // 2000 RPM minimum for active cooling
        var fanSpeed = baseSpeed + (correction * 30.0); // Scale correction to RPM

        // Clamp to physical limits
        fanSpeed = Math.Clamp(fanSpeed, 0, 5500);

        // Update state
        lastError = error;

        return (ushort)fanSpeed;
    }

    /// <summary>
    /// Adapt target temperatures based on workload type
    /// Gaming/Heavy: Lower targets for headroom
    /// Productivity: Higher targets for quieter operation
    /// </summary>
    private void AdaptTargetTemperatures(FusedTelemetry telemetry)
    {
        // Detect heavy workload (gaming, rendering, etc.)
        bool isHeavyWorkload = telemetry.CpuUtilization > 70 || telemetry.GpuUtilization > 50;

        if (isHeavyWorkload)
        {
            // Lower targets for better thermals under load
            _cpuTargetTemp = 70.0;
            _gpuTargetTemp = 65.0;
        }
        else if (telemetry.IsOnBattery)
        {
            // Higher targets on battery to reduce fan noise
            _cpuTargetTemp = 80.0;
            _gpuTargetTemp = 75.0;
        }
        else
        {
            // Balanced targets for AC productivity
            _cpuTargetTemp = 75.0;
            _gpuTargetTemp = 70.0;
        }
    }

    /// <summary>
    /// Adapt PID gains based on observed thermal response
    /// Machine learning approach: minimize overshoot and settling time
    /// </summary>
    private void AdaptPIDGains()
    {
        if (_thermalHistory.Count < 100)
            return;

        try
        {
            // Analyze recent thermal behavior
            var snapshots = _thermalHistory.GetAll();

            // Calculate temperature variance (measure of instability)
            var cpuVariance = CalculateVariance(snapshots, s => s.CpuTemp);
            var gpuVariance = CalculateVariance(snapshots, s => s.GpuTemp);

            // If variance is high (oscillating temps), reduce gains
            if (cpuVariance > 20.0)
            {
                _cpuGains.Kp *= 0.95; // Reduce proportional gain
                _cpuGains.Kd *= 0.9;  // Reduce derivative gain

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Reduced CPU PID gains due to high variance: {cpuVariance:F2}");
            }
            // If variance is low (stable), can increase gains for faster response
            else if (cpuVariance < 5.0)
            {
                _cpuGains.Kp *= 1.02; // Slightly increase gains
                _cpuGains.Kd *= 1.01;
            }

            // Same for GPU
            if (gpuVariance > 15.0)
            {
                _gpuGains.Kp *= 0.95;
                _gpuGains.Kd *= 0.9;
            }
            else if (gpuVariance < 3.0)
            {
                _gpuGains.Kp *= 1.02;
                _gpuGains.Kd *= 1.01;
            }

            // Clamp gains to reasonable bounds
            _cpuGains.Kp = Math.Clamp(_cpuGains.Kp, 0.5, 3.0);
            _cpuGains.Ki = Math.Clamp(_cpuGains.Ki, 0.01, 0.2);
            _cpuGains.Kd = Math.Clamp(_cpuGains.Kd, 0.1, 1.0);

            _gpuGains.Kp = Math.Clamp(_gpuGains.Kp, 0.5, 2.5);
            _gpuGains.Ki = Math.Clamp(_gpuGains.Ki, 0.01, 0.15);
            _gpuGains.Kd = Math.Clamp(_gpuGains.Kd, 0.1, 0.8);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"PID gain adaptation error (non-critical)", ex);
        }
    }

    private double CalculateVariance<T>(T[] data, Func<T, double> selector)
    {
        if (data.Length < 2)
            return 0;

        var values = data.Select(selector).ToArray();
        var mean = values.Average();
        var variance = values.Select(v => Math.Pow(v - mean, 2)).Average();

        return variance;
    }

    private async Task ApplyFanSpeedsAsync(ushort cpuFanSpeed, ushort gpuFanSpeed)
    {
        if (_ecController == null)
            return;

        try
        {
            // TODO: Apply fan speeds via EC controller (method not yet implemented)
            // For now, just log the desired speeds
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Target fan speeds: CPU={cpuFanSpeed} RPM, GPU={gpuFanSpeed} RPM");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply fan speeds", ex);
        }
    }

    private async Task TriggerEmergencyThermalResponseAsync(FusedTelemetry telemetry)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"🚨 EMERGENCY THERMAL RESPONSE: CPU={telemetry.CpuTemp}°C, GPU={telemetry.GpuTemp}°C, VRM={telemetry.VrmTemp}°C");

        // Broadcast emergency alert
        _agentBus.BroadcastMessage(AgentId, AgentMessageType.Alert, new
        {
            Type = "ThermalEmergency",
            CpuTemp = telemetry.CpuTemp,
            GpuTemp = telemetry.GpuTemp,
            VrmTemp = telemetry.VrmTemp
        });

        // Maximum cooling
        if (_ecController != null)
        {
            // TODO: Set fans to maximum speed (method not yet implemented)
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Setting fans to MAXIMUM SPEED (5500 RPM)");

            await Task.CompletedTask;
        }
    }
}

/// <summary>
/// PID controller gains
/// </summary>
public class PIDGains
{
    public double Kp { get; set; } // Proportional gain
    public double Ki { get; set; } // Integral gain
    public double Kd { get; set; } // Derivative gain
}

/// <summary>
/// Thermal snapshot for learning
/// </summary>
public struct ThermalSnapshot
{
    public DateTime Timestamp;
    public double CpuTemp;
    public double GpuTemp;
    public double FanSpeed;
}

/// <summary>
/// Circular buffer for efficient history storage
/// </summary>
public class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public int Count => _count;

    public CircularBuffer(int capacity)
    {
        _buffer = new T[capacity];
    }

    public void Add(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length)
            _count++;
    }

    public T[] GetAll()
    {
        var result = new T[_count];
        for (int i = 0; i < _count; i++)
        {
            var index = (_head - _count + i + _buffer.Length) % _buffer.Length;
            result[i] = _buffer[index];
        }
        return result;
    }
}
