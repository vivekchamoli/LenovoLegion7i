using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Phase 4: ML-based power usage prediction
/// Uses historical data to predict optimal power modes
/// </summary>
public class PowerUsagePredictor
{
    private readonly LinkedList<PowerUsageDataPoint> _history = new();
    private const int MaxHistorySize = 1000;
    private const int MinDataPoints = 50;

    public void RecordDataPoint(PowerUsageDataPoint dataPoint)
    {
        if (!FeatureFlags.UseMLAIController)
            return;

        _history.AddLast(dataPoint);

        // Maintain circular buffer
        if (_history.Count > MaxHistorySize)
            _history.RemoveFirst();
    }

    /// <summary>
    /// Predicts optimal power mode based on current conditions
    /// </summary>
    public PowerModeState? PredictOptimalPowerMode(
        int cpuUsagePercent,
        int cpuTemperature,
        bool isOnBattery,
        TimeSpan timeOfDay)
    {
        if (!FeatureFlags.UseMLAIController || _history.Count < MinDataPoints)
            return null;

        // Simple k-NN prediction (k=5)
        var neighbors = _history
            .Select(point => new
            {
                Point = point,
                Distance = CalculateDistance(point, cpuUsagePercent, cpuTemperature, isOnBattery, timeOfDay)
            })
            .OrderBy(x => x.Distance)
            .Take(5)
            .ToList();

        // Vote for power mode
        var votes = neighbors
            .GroupBy(x => x.Point.PowerMode)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        if (votes == null)
            return null;

        // Return prediction with confidence
        var confidence = (double)votes.Count() / neighbors.Count;
        return confidence >= 0.6 ? votes.Key : null;
    }

    /// <summary>
    /// Predicts CPU temperature in next 5 minutes
    /// </summary>
    public int? PredictCpuTemperature(int currentCpuUsage)
    {
        if (!FeatureFlags.UseMLAIController || _history.Count < MinDataPoints)
            return null;

        // Linear regression on recent data
        var recentData = _history
            .TakeLast(100)
            .Where(p => Math.Abs(p.CpuUsagePercent - currentCpuUsage) < 20)
            .ToList();

        if (recentData.Count < 10)
            return null;

        // Simple moving average with trend
        var avgTemp = (int)recentData.Average(p => p.CpuTemperature);
        var trend = CalculateTrend(recentData.Select(p => p.CpuTemperature).ToList());

        return avgTemp + (int)(trend * 5); // 5 minute projection
    }

    /// <summary>
    /// Suggests if user should switch power mode
    /// </summary>
    public PowerModeSuggestion GetPowerModeSuggestion(
        PowerModeState currentMode,
        int cpuUsagePercent,
        int cpuTemperature,
        bool isOnBattery,
        TimeSpan timeOfDay)
    {
        var predicted = PredictOptimalPowerMode(cpuUsagePercent, cpuTemperature, isOnBattery, timeOfDay);

        if (predicted == null || predicted == currentMode)
        {
            return new PowerModeSuggestion
            {
                ShouldSwitch = false,
                RecommendedMode = currentMode,
                Reason = "Current mode is optimal"
            };
        }

        // Check if prediction is stable (same for last 3 predictions)
        var recentPredictions = _history
            .TakeLast(3)
            .Select(p => PredictOptimalPowerMode(
                p.CpuUsagePercent,
                p.CpuTemperature,
                p.IsOnBattery,
                p.TimeOfDay))
            .ToList();

        var isStable = recentPredictions.All(p => p == predicted);

        if (!isStable)
        {
            return new PowerModeSuggestion
            {
                ShouldSwitch = false,
                RecommendedMode = currentMode,
                Reason = "Workload is unstable, waiting for pattern"
            };
        }

        return new PowerModeSuggestion
        {
            ShouldSwitch = true,
            RecommendedMode = predicted.Value,
            Reason = GetSwitchReason(currentMode, predicted.Value, cpuTemperature, isOnBattery)
        };
    }

    private double CalculateDistance(
        PowerUsageDataPoint point,
        int cpuUsage,
        int cpuTemp,
        bool isOnBattery,
        TimeSpan timeOfDay)
    {
        // Weighted euclidean distance
        var cpuUsageDiff = Math.Pow(point.CpuUsagePercent - cpuUsage, 2) * 2.0;
        var cpuTempDiff = Math.Pow(point.CpuTemperature - cpuTemp, 2) * 1.5;
        var batteryDiff = (point.IsOnBattery == isOnBattery) ? 0 : 100;
        var timeDiff = Math.Abs((point.TimeOfDay - timeOfDay).TotalMinutes) * 0.5;

        return Math.Sqrt(cpuUsageDiff + cpuTempDiff + batteryDiff + timeDiff);
    }

    private double CalculateTrend(List<int> values)
    {
        if (values.Count < 2)
            return 0;

        // Simple linear regression slope
        var n = values.Count;
        var sumX = Enumerable.Range(0, n).Sum();
        var sumY = values.Sum();
        var sumXY = Enumerable.Range(0, n).Zip(values, (x, y) => x * y).Sum();
        var sumX2 = Enumerable.Range(0, n).Sum(x => x * x);

        return (n * sumXY - sumX * sumY) / (double)(n * sumX2 - sumX * sumX);
    }

    private string GetSwitchReason(PowerModeState current, PowerModeState recommended, int cpuTemp, bool isOnBattery)
    {
        if (isOnBattery && recommended == PowerModeState.Quiet)
            return "On battery - switching to quiet mode to extend battery life";

        if (cpuTemp > 80 && recommended == PowerModeState.Quiet)
            return $"High CPU temperature ({cpuTemp}°C) - switching to quiet mode for better cooling";

        if (current == PowerModeState.Quiet && recommended == PowerModeState.Performance)
            return "High workload detected - switching to performance mode";

        return $"ML model suggests {recommended} based on usage pattern";
    }
}

public readonly struct PowerUsageDataPoint
{
    public PowerModeState PowerMode { get; init; }
    public int CpuUsagePercent { get; init; }
    public int CpuTemperature { get; init; }
    public bool IsOnBattery { get; init; }
    public TimeSpan TimeOfDay { get; init; }
    public DateTime Timestamp { get; init; }
}

public readonly struct PowerModeSuggestion
{
    public bool ShouldSwitch { get; init; }
    public PowerModeState RecommendedMode { get; init; }
    public string Reason { get; init; }
}
