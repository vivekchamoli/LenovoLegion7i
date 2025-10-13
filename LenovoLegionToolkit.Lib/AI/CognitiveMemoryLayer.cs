using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Cognitive Memory Layer - Autonomous learning and pattern recognition system
/// Stores historical performance patterns, workload classifications, and optimization effectiveness
///
/// Purpose: Enable agents to learn from past decisions and improve over time
/// </summary>
public class CognitiveMemoryLayer
{
    private readonly object _lock = new();
    private readonly List<WorkloadPattern> _workloadPatterns = new();
    private readonly List<OptimizationOutcome> _optimizationHistory = new();
    private readonly Dictionary<string, PerformanceMetric> _performanceMetrics = new();

    private const int MaxWorkloadPatterns = 1000;
    private const int MaxOptimizationHistory = 5000;
    private const double PatternSimilarityThreshold = 0.85; // 85% similarity to match patterns

    #region Pattern Recording

    /// <summary>
    /// Records a workload pattern for future reference
    /// </summary>
    public void RecordWorkloadPattern(WorkloadPattern pattern)
    {
        lock (_lock)
        {
            _workloadPatterns.Add(pattern);

            // Prune old patterns if limit exceeded
            if (_workloadPatterns.Count > MaxWorkloadPatterns)
            {
                // Remove oldest 10%
                var toRemove = _workloadPatterns.Count - MaxWorkloadPatterns;
                _workloadPatterns.RemoveRange(0, toRemove);
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[CognitiveMemory] Recorded workload pattern: {pattern.WorkloadType}, CPU={pattern.CpuUtilization:F1}%, GPU={pattern.GpuUtilization:F1}%");
        }
    }

    /// <summary>
    /// Records the outcome of an optimization attempt
    /// </summary>
    public void RecordOptimizationOutcome(OptimizationOutcome outcome)
    {
        lock (_lock)
        {
            _optimizationHistory.Add(outcome);

            // Prune old history if limit exceeded
            if (_optimizationHistory.Count > MaxOptimizationHistory)
            {
                var toRemove = _optimizationHistory.Count - MaxOptimizationHistory;
                _optimizationHistory.RemoveRange(0, toRemove);
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[CognitiveMemory] Recorded optimization: {outcome.ActionType}, Success={outcome.WasSuccessful}, Impact={outcome.PerformanceImpact:F2}");
        }
    }

    /// <summary>
    /// Updates a performance metric (e.g., optimization cycle time, agent proposal latency)
    /// </summary>
    public void UpdatePerformanceMetric(string metricName, double value, DateTime timestamp)
    {
        lock (_lock)
        {
            if (!_performanceMetrics.TryGetValue(metricName, out var metric))
            {
                metric = new PerformanceMetric { Name = metricName };
                _performanceMetrics[metricName] = metric;
            }

            metric.Values.Add((timestamp, value));
            metric.LastUpdated = timestamp;

            // Prune old values (keep last 24 hours)
            var cutoff = timestamp.AddHours(-24);
            metric.Values.RemoveAll(v => v.Timestamp < cutoff);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[CognitiveMemory] Updated metric: {metricName} = {value:F2}");
        }
    }

    #endregion

    #region Pattern Recognition

    /// <summary>
    /// Finds similar historical workload patterns
    /// </summary>
    public List<WorkloadPattern> FindSimilarPatterns(WorkloadPattern currentPattern, int maxResults = 10)
    {
        lock (_lock)
        {
            var similarities = _workloadPatterns
                .Select(p => new { Pattern = p, Similarity = CalculateSimilarity(currentPattern, p) })
                .Where(x => x.Similarity >= PatternSimilarityThreshold)
                .OrderByDescending(x => x.Similarity)
                .Take(maxResults)
                .Select(x => x.Pattern)
                .ToList();

            if (Log.Instance.IsTraceEnabled && similarities.Any())
                Log.Instance.Trace($"[CognitiveMemory] Found {similarities.Count} similar patterns to current workload");

            return similarities;
        }
    }

    /// <summary>
    /// Calculates similarity between two workload patterns (0.0 = no match, 1.0 = perfect match)
    /// </summary>
    private double CalculateSimilarity(WorkloadPattern a, WorkloadPattern b)
    {
        // Workload type must match
        if (a.WorkloadType != b.WorkloadType)
            return 0.0;

        // Calculate weighted similarity across dimensions
        var cpuSimilarity = 1.0 - Math.Abs(a.CpuUtilization - b.CpuUtilization) / 100.0;
        var gpuSimilarity = 1.0 - Math.Abs(a.GpuUtilization - b.GpuUtilization) / 100.0;
        var batterySimilarity = 1.0 - Math.Abs(a.BatteryPercent - b.BatteryPercent) / 100.0;
        var thermalSimilarity = 1.0 - Math.Abs(a.CpuTemperature - b.CpuTemperature) / 100.0;

        // Weighted average (CPU and GPU more important than battery/thermal)
        return (cpuSimilarity * 0.35 +
                gpuSimilarity * 0.35 +
                batterySimilarity * 0.15 +
                thermalSimilarity * 0.15);
    }

    #endregion

    #region Learning and Insights

    /// <summary>
    /// Gets the success rate of a specific action type
    /// </summary>
    public double GetActionSuccessRate(string actionType)
    {
        lock (_lock)
        {
            var outcomes = _optimizationHistory.Where(o => o.ActionType == actionType).ToList();
            if (outcomes.Count == 0)
                return 0.5; // Default 50% confidence if no history

            var successCount = outcomes.Count(o => o.WasSuccessful);
            return (double)successCount / outcomes.Count;
        }
    }

    /// <summary>
    /// Gets the average performance impact of a specific action type
    /// </summary>
    public double GetAveragePerformanceImpact(string actionType)
    {
        lock (_lock)
        {
            var outcomes = _optimizationHistory
                .Where(o => o.ActionType == actionType && o.WasSuccessful)
                .ToList();

            if (outcomes.Count == 0)
                return 0.0;

            return outcomes.Average(o => o.PerformanceImpact);
        }
    }

    /// <summary>
    /// Gets recommended actions based on similar historical patterns
    /// </summary>
    public List<string> GetRecommendedActions(WorkloadPattern currentPattern)
    {
        lock (_lock)
        {
            var similarPatterns = FindSimilarPatterns(currentPattern, 50);
            if (similarPatterns.Count == 0)
                return new List<string>();

            // Find actions that were successful for similar patterns
            var recommendations = _optimizationHistory
                .Where(o => similarPatterns.Any(p => p.Timestamp >= o.Timestamp.AddMinutes(-5) &&
                                                      p.Timestamp <= o.Timestamp.AddMinutes(5)))
                .Where(o => o.WasSuccessful && o.PerformanceImpact > 0)
                .GroupBy(o => o.ActionType)
                .OrderByDescending(g => g.Average(o => o.PerformanceImpact))
                .Select(g => g.Key)
                .Take(5)
                .ToList();

            if (Log.Instance.IsTraceEnabled && recommendations.Any())
                Log.Instance.Trace($"[CognitiveMemory] Recommended {recommendations.Count} actions based on historical patterns");

            return recommendations;
        }
    }

    /// <summary>
    /// Detects performance anomalies in a metric
    /// </summary>
    public bool DetectAnomaly(string metricName, double currentValue)
    {
        lock (_lock)
        {
            if (!_performanceMetrics.TryGetValue(metricName, out var metric))
                return false; // No history, can't detect anomalies

            if (metric.Values.Count < 10)
                return false; // Not enough data

            var values = metric.Values.Select(v => v.Value).ToList();
            var mean = values.Average();
            var stdDev = Math.Sqrt(values.Average(v => Math.Pow(v - mean, 2)));

            // Anomaly if value is more than 2 standard deviations from mean
            var isAnomaly = Math.Abs(currentValue - mean) > (2 * stdDev);

            if (isAnomaly && Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[CognitiveMemory] ANOMALY DETECTED: {metricName} = {currentValue:F2} (mean={mean:F2}, stdDev={stdDev:F2})");

            return isAnomaly;
        }
    }

    /// <summary>
    /// Gets performance metric statistics
    /// </summary>
    public PerformanceMetricStats? GetMetricStats(string metricName)
    {
        lock (_lock)
        {
            if (!_performanceMetrics.TryGetValue(metricName, out var metric))
                return null;

            if (metric.Values.Count == 0)
                return null;

            var values = metric.Values.Select(v => v.Value).ToList();
            var mean = values.Average();
            var stdDev = Math.Sqrt(values.Average(v => Math.Pow(v - mean, 2)));
            var min = values.Min();
            var max = values.Max();

            return new PerformanceMetricStats
            {
                Name = metricName,
                Count = values.Count,
                Mean = mean,
                StdDev = stdDev,
                Min = min,
                Max = max,
                LastValue = values.Last(),
                LastUpdated = metric.LastUpdated
            };
        }
    }

    #endregion

    #region Diagnostics

    /// <summary>
    /// Gets cognitive memory status for diagnostics
    /// </summary>
    public CognitiveMemoryStatus GetStatus()
    {
        lock (_lock)
        {
            return new CognitiveMemoryStatus
            {
                WorkloadPatternCount = _workloadPatterns.Count,
                OptimizationHistoryCount = _optimizationHistory.Count,
                PerformanceMetricCount = _performanceMetrics.Count,
                OldestPattern = _workloadPatterns.FirstOrDefault()?.Timestamp,
                NewestPattern = _workloadPatterns.LastOrDefault()?.Timestamp,
                MemoryUsageMB = EstimateMemoryUsage()
            };
        }
    }

    private double EstimateMemoryUsage()
    {
        // Rough estimate: ~200 bytes per pattern, ~300 bytes per outcome, ~500 bytes per metric
        var workloadMB = (_workloadPatterns.Count * 200) / (1024.0 * 1024.0);
        var outcomeMB = (_optimizationHistory.Count * 300) / (1024.0 * 1024.0);
        var metricMB = (_performanceMetrics.Count * 500) / (1024.0 * 1024.0);

        return workloadMB + outcomeMB + metricMB;
    }

    #endregion
}

#region Data Models

/// <summary>
/// Represents a workload pattern observed by the system
/// </summary>
public record WorkloadPattern
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public WorkloadType WorkloadType { get; init; }
    public double CpuUtilization { get; init; }
    public double GpuUtilization { get; init; }
    public int BatteryPercent { get; init; }
    public double CpuTemperature { get; init; }
    public double GpuTemperature { get; init; }
}

/// <summary>
/// Represents the outcome of an optimization attempt
/// </summary>
public record OptimizationOutcome
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string ActionType { get; init; } = string.Empty;
    public bool WasSuccessful { get; init; }
    public double PerformanceImpact { get; init; } // Positive = improvement, Negative = regression
    public string Notes { get; init; } = string.Empty;
}

/// <summary>
/// Performance metric with historical values
/// </summary>
public class PerformanceMetric
{
    public string Name { get; init; } = string.Empty;
    public List<(DateTime Timestamp, double Value)> Values { get; init; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}

/// <summary>
/// Statistical summary of a performance metric
/// </summary>
public record PerformanceMetricStats
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
    public double Mean { get; init; }
    public double StdDev { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double LastValue { get; init; }
    public DateTime LastUpdated { get; init; }
}

/// <summary>
/// Status of the cognitive memory system
/// </summary>
public record CognitiveMemoryStatus
{
    public int WorkloadPatternCount { get; init; }
    public int OptimizationHistoryCount { get; init; }
    public int PerformanceMetricCount { get; init; }
    public DateTime? OldestPattern { get; init; }
    public DateTime? NewestPattern { get; init; }
    public double MemoryUsageMB { get; init; }
}

#endregion
