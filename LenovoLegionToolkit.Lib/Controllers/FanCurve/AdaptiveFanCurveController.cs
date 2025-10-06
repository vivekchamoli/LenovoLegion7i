using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.AI;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.FanCurve;

/// <summary>
/// Phase 4: Adaptive fan curve with thermal learning
/// Learns optimal fan curves based on thermal performance
/// </summary>
public class AdaptiveFanCurveController
{
    private readonly Dictionary<int, FanCurveDataPoint> _thermalHistory = new();
    private readonly DataPersistenceService? _persistenceService;
    private const int MaxHistoryEntries = 500;
    private const int LearningThreshold = 50;
    private DateTime _lastPersistenceLoad = DateTime.MinValue;

    public AdaptiveFanCurveController(DataPersistenceService? persistenceService = null)
    {
        _persistenceService = persistenceService;
    }

    /// <summary>
    /// Records thermal performance for a given temperature
    /// </summary>
    public void RecordThermalPerformance(int temperature, int fanSpeed, int coolingEffectiveness)
    {
        if (!FeatureFlags.UseAdaptiveFanCurves)
            return;

        var key = temperature / 5 * 5; // Round to nearest 5°C

        if (!_thermalHistory.ContainsKey(key))
        {
            _thermalHistory[key] = new FanCurveDataPoint
            {
                Temperature = key,
                FanSpeed = fanSpeed,
                CoolingEffectiveness = coolingEffectiveness,
                SampleCount = 1
            };
        }
        else
        {
            var existing = _thermalHistory[key];
            _thermalHistory[key] = new FanCurveDataPoint
            {
                Temperature = key,
                FanSpeed = (existing.FanSpeed * existing.SampleCount + fanSpeed) / (existing.SampleCount + 1),
                CoolingEffectiveness = (existing.CoolingEffectiveness * existing.SampleCount + coolingEffectiveness) / (existing.SampleCount + 1),
                SampleCount = existing.SampleCount + 1
            };
        }

        // Maintain size limit
        if (_thermalHistory.Count > MaxHistoryEntries)
        {
            var oldest = _thermalHistory.OrderBy(x => x.Value.SampleCount).First().Key;
            _thermalHistory.Remove(oldest);
        }
    }

    /// <summary>
    /// Generates optimized fan curve based on learned thermal patterns
    /// </summary>
    public async Task<FanTableData?> GenerateAdaptiveFanCurveAsync(
        FanTableType tableType,
        byte fanId,
        byte sensorId,
        PowerModeState powerMode)
    {
        if (!FeatureFlags.UseAdaptiveFanCurves)
            return null;

        // Need minimum data points to generate curve
        if (_thermalHistory.Count < LearningThreshold)
            return null;

        await Task.CompletedTask;

        var fanSpeeds = new List<ushort>();
        var temps = new List<ushort>();

        // Generate 10-point curve from 30°C to 90°C
        for (int temp = 30; temp <= 90; temp += 6)
        {
            var fanSpeed = CalculateOptimalFanSpeed(temp, powerMode);
            temps.Add((ushort)temp);
            fanSpeeds.Add((ushort)fanSpeed);
        }

        return new FanTableData(tableType, fanId, sensorId, fanSpeeds.ToArray(), temps.ToArray());
    }

    /// <summary>
    /// Suggests fan speed adjustment based on current conditions
    /// </summary>
    public FanSpeedSuggestion SuggestFanSpeed(
        int currentTemp,
        int currentFanSpeed,
        int tempTrend, // positive = heating, negative = cooling
        PowerModeState powerMode)
    {
        if (!FeatureFlags.UseAdaptiveFanCurves)
        {
            return new FanSpeedSuggestion
            {
                ShouldAdjust = false,
                RecommendedFanSpeed = currentFanSpeed,
                Reason = "Adaptive fan curves disabled"
            };
        }

        var optimalSpeed = CalculateOptimalFanSpeed(currentTemp, powerMode);

        // Add predictive adjustment based on temperature trend
        if (tempTrend > 2) // Heating fast
        {
            optimalSpeed = Math.Min(100, optimalSpeed + 15);
        }
        else if (tempTrend < -2) // Cooling fast
        {
            optimalSpeed = Math.Max(30, optimalSpeed - 10);
        }

        var speedDiff = Math.Abs(optimalSpeed - currentFanSpeed);

        if (speedDiff < 5)
        {
            return new FanSpeedSuggestion
            {
                ShouldAdjust = false,
                RecommendedFanSpeed = currentFanSpeed,
                Reason = "Current fan speed is optimal"
            };
        }

        return new FanSpeedSuggestion
        {
            ShouldAdjust = true,
            RecommendedFanSpeed = optimalSpeed,
            Reason = GetAdjustmentReason(currentTemp, tempTrend, optimalSpeed, currentFanSpeed)
        };
    }

    /// <summary>
    /// Calculates cooling effectiveness score (0-100)
    /// </summary>
    public int CalculateCoolingEffectiveness(
        int tempBefore,
        int tempAfter,
        int fanSpeed,
        TimeSpan duration)
    {
        var tempDrop = tempBefore - tempAfter;
        var expectedDrop = (fanSpeed / 100.0) * (duration.TotalSeconds / 60.0) * 10; // Rough estimate

        if (expectedDrop <= 0)
            return 50;

        var effectiveness = (tempDrop / expectedDrop) * 100;
        return Math.Clamp((int)effectiveness, 0, 100);
    }

    private int CalculateOptimalFanSpeed(int temperature, PowerModeState powerMode)
    {
        // Base curve from learned data
        var nearbyPoints = _thermalHistory
            .Where(x => Math.Abs(x.Key - temperature) <= 10)
            .OrderBy(x => Math.Abs(x.Key - temperature))
            .Take(3)
            .ToList();

        int baseFanSpeed;
        if (nearbyPoints.Any())
        {
            baseFanSpeed = (int)nearbyPoints.Average(x => x.Value.FanSpeed);
        }
        else
        {
            // Fallback linear curve if no data
            baseFanSpeed = Math.Clamp((temperature - 30) * 2, 30, 100);
        }

        // Adjust based on power mode
        return powerMode switch
        {
            PowerModeState.Quiet => Math.Max(30, baseFanSpeed - 10),
            PowerModeState.Balance => baseFanSpeed,
            PowerModeState.Performance => Math.Min(100, baseFanSpeed + 10),
            _ => baseFanSpeed
        };
    }

    private string GetAdjustmentReason(int temp, int trend, int recommended, int current)
    {
        if (trend > 2)
            return $"Temperature rising rapidly ({temp}°C) - increasing fan to {recommended}%";

        if (trend < -2)
            return $"Temperature dropping ({temp}°C) - reducing fan to {recommended}% for quieter operation";

        if (temp > 80)
            return $"High temperature ({temp}°C) - increasing fan to {recommended}% for safety";

        if (temp < 50 && current > 50)
            return $"Low temperature ({temp}°C) - reducing fan to {recommended}% to save power";

        return $"Learned optimal fan speed for {temp}°C is {recommended}%";
    }

    /// <summary>
    /// Get current data point count for UI/diagnostics
    /// </summary>
    public int GetDataPointCount()
    {
        return _thermalHistory.Values.Sum(x => x.SampleCount);
    }

    /// <summary>
    /// Get total number of unique temperature points tracked
    /// </summary>
    public int GetUniqueTemperaturePoints()
    {
        return _thermalHistory.Count;
    }

    /// <summary>
    /// Load thermal training data from persistence service
    /// Should be called during initialization
    /// </summary>
    public async Task LoadThermalTrainingDataAsync()
    {
        if (_persistenceService == null || !FeatureFlags.UseAdaptiveFanCurves)
            return;

        try
        {
            var trainingData = await _persistenceService.LoadThermalTrainingDataAsync().ConfigureAwait(false);

            if (trainingData.Count == 0)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"No thermal training data to load");
                return;
            }

            // Rebuild thermal history from training data
            _thermalHistory.Clear();
            foreach (var dataPoint in trainingData)
            {
                RecordThermalPerformance(
                    dataPoint.TempBefore,
                    dataPoint.FanSpeedBefore * 100 / 255, // Convert 0-255 to percentage
                    dataPoint.CoolingEffectiveness
                );
            }

            _lastPersistenceLoad = DateTime.UtcNow;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Loaded {trainingData.Count} thermal training data points, created {_thermalHistory.Count} unique temperature entries");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to load thermal training data", ex);
        }
    }

    /// <summary>
    /// Export thermal history as training data for persistence
    /// </summary>
    public List<ThermalTrainingDataPoint> ExportThermalHistory()
    {
        var exportData = new List<ThermalTrainingDataPoint>();

        foreach (var kvp in _thermalHistory)
        {
            exportData.Add(new ThermalTrainingDataPoint
            {
                Timestamp = DateTime.UtcNow,
                TempBefore = (byte)kvp.Value.Temperature,
                TempAfter = (byte)kvp.Value.Temperature, // Approximation
                FanSpeedBefore = (byte)(kvp.Value.FanSpeed * 255 / 100),
                FanSpeedAfter = (byte)(kvp.Value.FanSpeed * 255 / 100),
                Workload = WorkloadType.Unknown,
                PowerLevel = 0,
                CoolingEffectiveness = kvp.Value.CoolingEffectiveness,
                DurationSeconds = 60
            });
        }

        return exportData;
    }

    /// <summary>
    /// Clear all learned thermal data (for reset/testing)
    /// </summary>
    public void ClearLearningData()
    {
        _thermalHistory.Clear();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Cleared all adaptive fan curve learning data");
    }

    /// <summary>
    /// Get learning statistics for diagnostics
    /// </summary>
    public AdaptiveLearningStats GetLearningStats()
    {
        var totalSamples = _thermalHistory.Values.Sum(x => x.SampleCount);
        var avgEffectiveness = _thermalHistory.Values.Any()
            ? _thermalHistory.Values.Average(x => x.CoolingEffectiveness)
            : 0;

        return new AdaptiveLearningStats
        {
            TotalDataPoints = totalSamples,
            UniqueTemperatures = _thermalHistory.Count,
            AverageCoolingEffectiveness = avgEffectiveness,
            IsLearningEnabled = FeatureFlags.UseAdaptiveFanCurves,
            HasSufficientData = totalSamples >= LearningThreshold,
            LastDataLoadTime = _lastPersistenceLoad
        };
    }
}

public struct FanCurveDataPoint
{
    public int Temperature { get; init; }
    public int FanSpeed { get; init; }
    public int CoolingEffectiveness { get; init; }
    public int SampleCount { get; init; }
}

public readonly struct FanSpeedSuggestion
{
    public bool ShouldAdjust { get; init; }
    public int RecommendedFanSpeed { get; init; }
    public string Reason { get; init; }
}

/// <summary>
/// Statistics about adaptive fan curve learning progress
/// </summary>
public readonly struct AdaptiveLearningStats
{
    public int TotalDataPoints { get; init; }
    public int UniqueTemperatures { get; init; }
    public double AverageCoolingEffectiveness { get; init; }
    public bool IsLearningEnabled { get; init; }
    public bool HasSufficientData { get; init; }
    public DateTime LastDataLoadTime { get; init; }

    public override string ToString()
    {
        return $"Adaptive Learning: {TotalDataPoints} samples across {UniqueTemperatures} temps, Avg Effectiveness: {AverageCoolingEffectiveness:F1}%, Sufficient: {HasSufficientData}";
    }
}
