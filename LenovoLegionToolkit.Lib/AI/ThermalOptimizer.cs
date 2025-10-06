using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Advanced AI-powered thermal management for Legion Slim 7i Gen 9
/// Uses predictive algorithms to prevent thermal throttling and optimize performance
/// </summary>
public class ThermalOptimizer
{
    private readonly Gen9ECController _ecController;
    private readonly List<ThermalState> _thermalHistory = new();
    private readonly object _historyLock = new();
    private const int MaxHistorySize = 300; // 5 minutes at 1Hz sampling
    private const int PredictionHorizonSeconds = 60;

    public ThermalOptimizer(Gen9ECController ecController)
    {
        _ecController = ecController ?? throw new ArgumentNullException(nameof(ecController));
    }

    /// <summary>
    /// Optimize thermal performance in real-time for current workload
    /// </summary>
    public async Task<ThermalOptimizationResult> OptimizeThermalPerformanceAsync(WorkloadType workloadType)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting thermal optimization for workload: {workloadType}");

        var optimizationResult = new ThermalOptimizationResult
        {
            WorkloadType = workloadType,
            StartTime = DateTime.UtcNow
        };

        try
        {
            // Collect current thermal state
            var currentState = await CollectThermalStateAsync();

            // Add to history
            lock (_historyLock)
            {
                _thermalHistory.Add(currentState);
                while (_thermalHistory.Count > MaxHistorySize)
                    _thermalHistory.RemoveAt(0);
            }

            // Predict future thermal state
            var predictions = PredictThermalState(_thermalHistory, PredictionHorizonSeconds);

            // Generate workload-specific optimizations
            var settings = workloadType switch
            {
                WorkloadType.Gaming => OptimizeForGaming(predictions),
                WorkloadType.HeavyProductivity => OptimizeForProductivity(predictions),
                WorkloadType.LightProductivity => OptimizeForProductivity(predictions),
                WorkloadType.AIWorkload => OptimizeForAI(predictions),
                _ => OptimizeBalanced(predictions)
            };

            // Apply optimizations
            await ApplyOptimizationsAsync(settings);

            optimizationResult.AppliedSettings = settings;
            optimizationResult.PredictedTemperatures = predictions;
            optimizationResult.ThrottleRisk = CalculateThrottleRisk(predictions);
            optimizationResult.Recommendations = GenerateRecommendations(predictions, currentState);
            optimizationResult.Success = true;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Thermal optimization completed successfully. Throttle risk: {optimizationResult.ThrottleRisk:P1}");

        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Thermal optimization failed", ex);

            optimizationResult.Success = false;
            optimizationResult.ErrorMessage = ex.Message;
        }

        optimizationResult.EndTime = DateTime.UtcNow;
        return optimizationResult;
    }

    /// <summary>
    /// Apply a specific fan profile directly
    /// </summary>
    public async Task ApplyFanProfileAsync(FanProfile profile)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Applying fan profile: {profile}");

        switch (profile)
        {
            case FanProfile.Quiet:
                await ApplyQuietFanProfileAsync();
                break;
            case FanProfile.Balanced:
                await _ecController.ApplyBalancedFanBehaviorAsync();
                break;
            case FanProfile.MaxPerformance:
                await ApplyMaxPerformanceFanProfileAsync();
                break;
            default:
                await _ecController.ApplyBalancedFanBehaviorAsync();
                break;
        }
    }

    /// <summary>
    /// Collect current thermal state from Gen 9 sensors
    /// </summary>
    private async Task<ThermalState> CollectThermalStateAsync()
    {
        var sensorData = await _ecController.ReadSensorDataAsync();

        return new ThermalState
        {
            CpuTemp = sensorData.CpuPackageTemp,
            GpuTemp = sensorData.GpuTemp,
            GpuHotspot = sensorData.GpuHotspot,
            GpuMemoryTemp = sensorData.GpuMemoryTemp,
            VrmTemp = sensorData.VrmTemp,
            SsdTemp = sensorData.SsdTemp,
            Fan1Speed = sensorData.Fan1Speed,
            Fan2Speed = sensorData.Fan2Speed,
            AmbientTemp = 25, // Estimated
            Timestamp = sensorData.Timestamp,
            Trend = new ThermalTrend { IsStable = true }
        };
    }

    /// <summary>
    /// Predict future thermal state using trend analysis
    /// </summary>
    public ThermalPredictions PredictThermalState(List<ThermalState> history, int secondsAhead)
    {
        if (history.Count < 5)
            return GetDefaultPredictions();

        var recentHistory = history.TakeLast(30).ToList(); // Last 30 seconds

        // Calculate temperature trends
        var cpuTrend = CalculateTemperatureTrend(recentHistory.Select(h => h.CpuTemp).ToList());
        var gpuTrend = CalculateTemperatureTrend(recentHistory.Select(h => h.GpuTemp).ToList());

        var currentState = history.Last();

        return new ThermalPredictions
        {
            PredictedCpuTemp = Math.Max(0, currentState.CpuTemp + (cpuTrend * secondsAhead)),
            PredictedGpuTemp = Math.Max(0, currentState.GpuTemp + (gpuTrend * secondsAhead)),
            PredictedGpuHotspot = Math.Max(0, currentState.GpuHotspot + (gpuTrend * secondsAhead * 1.2)),
            PredictedVrmTemp = Math.Max(0, currentState.VrmTemp + ((cpuTrend + gpuTrend) * 0.5 * secondsAhead)),
            Confidence = CalculatePredictionConfidence(recentHistory)
        };
    }

    /// <summary>
    /// Calculate temperature trend (°C per second)
    /// </summary>
    private double CalculateTemperatureTrend(List<byte> temperatures)
    {
        if (temperatures.Count < 2)
            return 0;

        var x = Enumerable.Range(0, temperatures.Count).Select(i => (double)i).ToArray();
        var y = temperatures.Select(t => (double)t).ToArray();

        // Simple linear regression
        var n = temperatures.Count;
        var sumX = x.Sum();
        var sumY = y.Sum();
        var sumXY = x.Zip(y, (xi, yi) => xi * yi).Sum();
        var sumX2 = x.Select(xi => xi * xi).Sum();

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        return slope;
    }

    /// <summary>
    /// Gaming-specific optimizations
    /// </summary>
    private OptimizationSettings OptimizeForGaming(ThermalPredictions predictions)
    {
        return new OptimizationSettings
        {
            CpuPL1 = 55,  // Base power
            CpuPL2 = 140, // Turbo power
            GpuTGP = 140, // Max GPU power
            FanProfile = FanProfile.Aggressive,
            VaporChamberMode = VaporChamberMode.Enhanced,  // Enhanced vapor chamber for sustained gaming
            Recommendations = new List<string>
            {
                "Enable GPU overclock +150MHz core, +500MHz memory",
                "Set Windows to High Performance mode",
                "Disable CPU E-cores for gaming",
                "Enable Resizable BAR",
                "Vapor chamber in Enhanced mode for optimal heat dissipation"
            }
        };
    }

    /// <summary>
    /// Productivity workload optimizations
    /// </summary>
    private OptimizationSettings OptimizeForProductivity(ThermalPredictions predictions)
    {
        return new OptimizationSettings
        {
            CpuPL1 = 65,  // Higher base for sustained loads
            CpuPL2 = 115, // Lower turbo for consistency
            GpuTGP = 60,  // Reduced GPU power
            FanProfile = FanProfile.Quiet,
            VaporChamberMode = VaporChamberMode.Standard,  // Standard mode for balanced efficiency
            Recommendations = new List<string>
            {
                "Enable all CPU cores",
                "Optimize for battery life",
                "Enable Intel Speed Shift",
                "Vapor chamber in Standard mode for quiet operation"
            }
        };
    }

    /// <summary>
    /// AI/ML workload optimizations
    /// </summary>
    private OptimizationSettings OptimizeForAI(ThermalPredictions predictions)
    {
        return new OptimizationSettings
        {
            CpuPL1 = 45,  // Lower CPU power
            CpuPL2 = 90,
            GpuTGP = 140, // Maximum GPU power for CUDA
            FanProfile = FanProfile.MaxPerformance,
            VaporChamberMode = VaporChamberMode.Maximum,  // Maximum vapor chamber for sustained AI workloads
            Recommendations = new List<string>
            {
                "Enable CUDA acceleration",
                "Set GPU to Prefer Maximum Performance",
                "Enable GPU memory overclocking",
                "Disable GPU power saving features",
                "Vapor chamber in Maximum mode for extreme cooling"
            }
        };
    }

    /// <summary>
    /// Balanced optimizations
    /// </summary>
    private OptimizationSettings OptimizeBalanced(ThermalPredictions predictions)
    {
        var throttleRisk = CalculateThrottleRisk(predictions);

        if (throttleRisk > 0.7)
        {
            // High throttle risk - reduce power
            return new OptimizationSettings
            {
                CpuPL1 = 45,
                CpuPL2 = 100,
                GpuTGP = 100,
                FanProfile = FanProfile.Aggressive,
                Recommendations = new List<string> { "Reducing power to prevent throttling" }
            };
        }
        else if (throttleRisk < 0.3)
        {
            // Low throttle risk - increase performance
            return new OptimizationSettings
            {
                CpuPL1 = 55,
                CpuPL2 = 130,
                GpuTGP = 130,
                FanProfile = FanProfile.Balanced,
                Recommendations = new List<string> { "Increasing performance - low thermal risk" }
            };
        }

        return new OptimizationSettings
        {
            CpuPL1 = 50,
            CpuPL2 = 115,
            GpuTGP = 115,
            FanProfile = FanProfile.Balanced,
            Recommendations = new List<string> { "Maintaining balanced performance" }
        };
    }

    /// <summary>
    /// Apply optimization settings to hardware
    /// </summary>
    private async Task ApplyOptimizationsAsync(OptimizationSettings settings)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Applying optimization settings: PL1={settings.CpuPL1}W, PL2={settings.CpuPL2}W, GPU TGP={settings.GpuTGP}W, VaporChamber={settings.VaporChamberMode}");

        await _ecController.SetPowerLimitsAsync(settings.CpuPL1, settings.CpuPL2, settings.GpuTGP);

        // Apply vapor chamber mode
        await _ecController.SetVaporChamberModeAsync(settings.VaporChamberMode);

        // Apply fan profile if needed
        switch (settings.FanProfile)
        {
            case FanProfile.Aggressive:
                await _ecController.FixFanCurveAsync(); // Use optimized curve
                break;
            case FanProfile.Quiet:
                await ApplyQuietFanProfileAsync();
                break;
            case FanProfile.MaxPerformance:
                await ApplyMaxPerformanceFanProfileAsync();
                break;
        }
    }

    /// <summary>
    /// Apply quiet fan profile - prioritizes silence over cooling
    /// Acoustic-optimized curve with gentle ramps and high hysteresis
    /// </summary>
    private async Task ApplyQuietFanProfileAsync()
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Applying Quiet fan profile...");

            // Temperature points (10 points)
            var tempPoints = new byte[]
            {
                30,  // 30°C
                40,  // 40°C
                50,  // 50°C
                55,  // 55°C - start fans here
                60,  // 60°C
                70,  // 70°C
                75,  // 75°C
                80,  // 80°C
                85,  // 85°C
                95   // 95°C
            };

            // Quiet fan curve: Prioritize silence, allow higher temps
            // Strategy: Keep fans off as long as possible, gentle ramps when needed
            // Values in 0-255 range
            var quietCpuSpeeds = new byte[]
            {
                0,    // 30°C: 0% (zero RPM)
                0,    // 40°C: 0% (zero RPM)
                0,    // 50°C: 0% (zero RPM - extended silence)
                38,   // 55°C: 15% (very gentle start)
                64,   // 60°C: 25% (still conservative)
                102,  // 70°C: 40% (moderate)
                140,  // 75°C: 55% (ramping up)
                179,  // 80°C: 70% (safety priority)
                217,  // 85°C: 85% (high priority)
                255   // 95°C: 100% (emergency)
            };

            // GPU fan same curve for consistency
            var quietGpuSpeeds = quietCpuSpeeds;

            // Write temperature points
            for (int i = 0; i < tempPoints.Length; i++)
            {
                await _ecController.WriteRegisterAsync((byte)(0xC0 + i), tempPoints[i]);
            }

            // Write CPU and GPU fan speeds
            for (int i = 0; i < quietCpuSpeeds.Length; i++)
            {
                await _ecController.WriteRegisterAsync((byte)(0xB4 + i), quietCpuSpeeds[i]);  // CPU fan
                await _ecController.WriteRegisterAsync((byte)(0xB5 + i), quietGpuSpeeds[i]);  // GPU fan
            }

            // Extended zero RPM mode with higher threshold (55°C)
            await _ecController.SetZeroRPMEnabledAsync(true, 55);

            // Slow fan acceleration for acoustic smoothness (3 seconds ramp)
            await _ecController.SetFanAccelerationAsync(30);  // 30 = 3 second ramp

            // High hysteresis to prevent oscillation (8°C delta)
            await _ecController.SetFanHysteresisAsync(8);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Quiet fan profile applied - prioritizing silence (zero RPM up to 55°C)");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply Quiet fan profile", ex);
        }
    }

    /// <summary>
    /// Apply max performance fan profile - prioritizes cooling over noise
    /// Aggressive curve with fast response and low hysteresis
    /// </summary>
    private async Task ApplyMaxPerformanceFanProfileAsync()
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Applying Max Performance fan profile...");

            // Temperature points (10 points)
            var tempPoints = new byte[]
            {
                30,  // 30°C
                40,  // 40°C
                45,  // 45°C
                50,  // 50°C
                55,  // 55°C
                60,  // 60°C
                65,  // 65°C
                70,  // 70°C
                75,  // 75°C
                80   // 80°C (already at max before throttle)
            };

            // Max Performance curve: Aggressive cooling, thermal headroom for boost
            // Strategy: Keep temps as low as possible for sustained turbo boost
            // Values in 0-255 range
            var maxCpuSpeeds = new byte[]
            {
                51,   // 30°C: 20% (baseline cooling - no zero RPM)
                77,   // 40°C: 30% (proactive)
                102,  // 45°C: 40% (ramping up)
                128,  // 50°C: 50% (moderate)
                153,  // 55°C: 60% (aggressive)
                179,  // 60°C: 70% (high cooling)
                204,  // 65°C: 80% (very aggressive)
                217,  // 70°C: 85% (maximum cooling)
                230,  // 75°C: 90% (near full speed)
                255   // 80°C: 100% (full blast)
            };

            // GPU fan slightly less aggressive for balance
            var maxGpuSpeeds = new byte[]
            {
                38,   // 30°C: 15%
                64,   // 40°C: 25%
                89,   // 45°C: 35%
                115,  // 50°C: 45%
                140,  // 55°C: 55%
                166,  // 60°C: 65%
                191,  // 65°C: 75%
                204,  // 70°C: 80%
                217,  // 75°C: 85%
                255   // 80°C: 100%
            };

            // Write temperature points
            for (int i = 0; i < tempPoints.Length; i++)
            {
                await _ecController.WriteRegisterAsync((byte)(0xC0 + i), tempPoints[i]);
            }

            // Write fan curves
            for (int i = 0; i < maxCpuSpeeds.Length; i++)
            {
                await _ecController.WriteRegisterAsync((byte)(0xB4 + i), maxCpuSpeeds[i]);  // CPU fan
                await _ecController.WriteRegisterAsync((byte)(0xB5 + i), maxGpuSpeeds[i]);  // GPU fan
            }

            // Disable zero RPM mode - always spin fans for maximum cooling
            await _ecController.SetZeroRPMEnabledAsync(false);

            // Fast fan acceleration for immediate response (0.5 seconds ramp)
            await _ecController.SetFanAccelerationAsync(5);

            // Low hysteresis for quick response (2°C delta)
            await _ecController.SetFanHysteresisAsync(2);

            // Enable vapor chamber maximum mode for Gen 9
            await _ecController.SetVaporChamberModeAsync(VaporChamberMode.Maximum);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Max Performance fan profile applied - prioritizing cooling (no zero RPM, fast response)");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply Max Performance fan profile", ex);
        }
    }

    /// <summary>
    /// Calculate throttle risk based on predictions
    /// </summary>
    private double CalculateThrottleRisk(ThermalPredictions predictions)
    {
        var risks = new List<double>();

        // CPU throttle risk (100°C limit)
        if (predictions.PredictedCpuTemp >= 100)
            risks.Add(1.0);
        else if (predictions.PredictedCpuTemp >= 95)
            risks.Add((predictions.PredictedCpuTemp - 95) / 5.0);
        else
            risks.Add(0.0);

        // GPU throttle risk (87°C limit)
        if (predictions.PredictedGpuTemp >= 87)
            risks.Add(1.0);
        else if (predictions.PredictedGpuTemp >= 82)
            risks.Add((predictions.PredictedGpuTemp - 82) / 5.0);
        else
            risks.Add(0.0);

        return risks.Max();
    }

    /// <summary>
    /// Generate actionable recommendations
    /// </summary>
    private List<string> GenerateRecommendations(ThermalPredictions predictions, ThermalState currentState)
    {
        var recommendations = new List<string>();

        if (predictions.PredictedCpuTemp > 90)
            recommendations.Add("CPU running hot - consider reducing workload or improving ventilation");

        if (predictions.PredictedGpuTemp > 80)
            recommendations.Add("GPU thermal limit approaching - reduce graphics settings or enable more aggressive fan curve");

        if (currentState.SsdTemp > 70)
            recommendations.Add("SSD temperature elevated - ensure adequate case ventilation");

        if (predictions.Confidence < 0.5)
            recommendations.Add("Thermal predictions have low confidence - continuing to gather data");

        return recommendations;
    }

    private double CalculatePredictionConfidence(List<ThermalState> history)
    {
        if (history.Count < 10)
            return 0.3;

        // Calculate temperature variance - higher variance = lower confidence
        var cpuVariance = CalculateVariance(history.Select(h => (double)h.CpuTemp).ToList());
        var gpuVariance = CalculateVariance(history.Select(h => (double)h.GpuTemp).ToList());

        var avgVariance = (cpuVariance + gpuVariance) / 2.0;

        // Convert variance to confidence (inverse relationship)
        return Math.Max(0.1, Math.Min(1.0, 1.0 - (avgVariance / 100.0)));
    }

    private double CalculateVariance(List<double> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        var variance = values.Select(v => Math.Pow(v - mean, 2)).Average();
        return variance;
    }

    private ThermalPredictions GetDefaultPredictions()
    {
        return new ThermalPredictions
        {
            PredictedCpuTemp = 70,
            PredictedGpuTemp = 65,
            PredictedGpuHotspot = 75,
            PredictedVrmTemp = 65,
            Confidence = 0.5
        };
    }
}

#region Data Structures

/// <summary>
/// Thermal predictions from optimizer
/// </summary>
public struct ThermalPredictions
{
    public double PredictedCpuTemp { get; set; }
    public double PredictedGpuTemp { get; set; }
    public double PredictedGpuHotspot { get; set; }
    public double PredictedVrmTemp { get; set; }
    public double Confidence { get; set; }
}

/// <summary>
/// Optimization settings
/// </summary>
public class OptimizationSettings
{
    public int CpuPL1 { get; set; }
    public int CpuPL2 { get; set; }
    public int GpuTGP { get; set; }
    public FanProfile FanProfile { get; set; }
    public VaporChamberMode VaporChamberMode { get; set; } = VaporChamberMode.Standard;
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// Thermal optimization result
/// </summary>
public class ThermalOptimizationResult
{
    public WorkloadType WorkloadType { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public OptimizationSettings? AppliedSettings { get; set; }
    public ThermalPredictions PredictedTemperatures { get; set; }
    public double ThrottleRisk { get; set; }
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// Fan profile types
/// </summary>
public enum FanProfile
{
    Quiet,
    Balanced,
    Aggressive,
    MaxPerformance
}

/// <summary>
/// Vapor chamber cooling modes for Gen 9 Legion 7i
/// Different modes optimize heat dissipation for specific workloads
/// </summary>
public enum VaporChamberMode
{
    /// <summary>
    /// Standard mode - balanced thermal transfer (0x00)
    /// Best for: General use, light productivity
    /// Power consumption: Low
    /// </summary>
    Standard = 0x00,

    /// <summary>
    /// Enhanced mode - increased vapor circulation (0x02)
    /// Best for: Gaming, moderate workloads
    /// Power consumption: Medium
    /// </summary>
    Enhanced = 0x02,

    /// <summary>
    /// Maximum mode - aggressive vapor chamber circulation (0x03)
    /// Best for: Heavy rendering, AI workloads, sustained high power
    /// Power consumption: High
    /// </summary>
    Maximum = 0x03,

    /// <summary>
    /// Eco mode - minimal vapor chamber activity (0x01)
    /// Best for: Battery operation, low-power scenarios
    /// Power consumption: Very low
    /// </summary>
    Eco = 0x01
}

#endregion