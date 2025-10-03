using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Performance monitoring and telemetry for optimization tracking
/// </summary>
public class PerformanceMonitor
{
    private readonly ConcurrentDictionary<string, OperationMetrics> _metrics = new();
    private readonly ConcurrentQueue<SlowOperation> _slowOperations = new();
    private const int MaxSlowOperationsHistory = 100;

    public class OperationMetrics
    {
        public string OperationName { get; init; } = string.Empty;
        public long TotalCalls { get; set; }
        public long TotalMilliseconds { get; set; }
        public long MinMilliseconds { get; set; } = long.MaxValue;
        public long MaxMilliseconds { get; set; }
        public long FailureCount { get; set; }

        public double AverageMilliseconds => TotalCalls > 0 ? (double)TotalMilliseconds / TotalCalls : 0;
    }

    public class SlowOperation
    {
        public string OperationName { get; init; } = string.Empty;
        public long DurationMs { get; init; }
        public DateTime Timestamp { get; init; }
        public Dictionary<string, object> Tags { get; init; } = new();
    }

    /// <summary>
    /// Measure async operation performance
    /// </summary>
    public async Task<T> MeasureAsync<T>(
        string operationName,
        Func<Task<T>> operation,
        Dictionary<string, object>? tags = null,
        long slowThresholdMs = 100)
    {
        if (!FeatureFlags.EnableTelemetry)
            return await operation().ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        var success = false;

        try
        {
            var result = await operation().ConfigureAwait(false);
            success = true;
            return result;
        }
        finally
        {
            sw.Stop();
            RecordMetric(operationName, sw.ElapsedMilliseconds, success, tags, slowThresholdMs);
        }
    }

    /// <summary>
    /// Measure sync operation performance
    /// </summary>
    public T Measure<T>(
        string operationName,
        Func<T> operation,
        Dictionary<string, object>? tags = null,
        long slowThresholdMs = 100)
    {
        if (!FeatureFlags.EnableTelemetry)
            return operation();

        var sw = Stopwatch.StartNew();
        var success = false;

        try
        {
            var result = operation();
            success = true;
            return result;
        }
        finally
        {
            sw.Stop();
            RecordMetric(operationName, sw.ElapsedMilliseconds, success, tags, slowThresholdMs);
        }
    }

    /// <summary>
    /// Record operation metric
    /// </summary>
    private void RecordMetric(
        string operationName,
        long durationMs,
        bool success,
        Dictionary<string, object>? tags,
        long slowThresholdMs)
    {
        var metrics = _metrics.GetOrAdd(operationName, _ => new OperationMetrics { OperationName = operationName });

        metrics.TotalCalls++;
        metrics.TotalMilliseconds += durationMs;

        if (durationMs < metrics.MinMilliseconds)
            metrics.MinMilliseconds = durationMs;

        if (durationMs > metrics.MaxMilliseconds)
            metrics.MaxMilliseconds = durationMs;

        if (!success)
            metrics.FailureCount++;

        // Track slow operations
        if (durationMs > slowThresholdMs)
        {
            var slowOp = new SlowOperation
            {
                OperationName = operationName,
                DurationMs = durationMs,
                Timestamp = DateTime.UtcNow,
                Tags = tags ?? new Dictionary<string, object>()
            };

            _slowOperations.Enqueue(slowOp);

            // Keep only recent slow operations
            while (_slowOperations.Count > MaxSlowOperationsHistory)
                _slowOperations.TryDequeue(out _);

            // Log slow operation
            if (Log.Instance.IsTraceEnabled)
            {
                var tagsStr = tags != null
                    ? string.Join(", ", tags.Select(kvp => $"{kvp.Key}={kvp.Value}"))
                    : "none";

                Log.Instance.Trace($"SLOW OPERATION: {operationName} took {durationMs}ms (threshold: {slowThresholdMs}ms) [tags: {tagsStr}]");
            }
        }
    }

    /// <summary>
    /// Get all operation metrics
    /// </summary>
    public IReadOnlyDictionary<string, OperationMetrics> GetAllMetrics()
    {
        return _metrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Get slow operations within time window
    /// </summary>
    public IEnumerable<SlowOperation> GetSlowOperations(TimeSpan timeWindow)
    {
        var cutoff = DateTime.UtcNow.Subtract(timeWindow);
        return _slowOperations.Where(op => op.Timestamp >= cutoff).OrderByDescending(op => op.Timestamp);
    }

    /// <summary>
    /// Get metrics for specific operation
    /// </summary>
    public OperationMetrics? GetMetrics(string operationName)
    {
        return _metrics.TryGetValue(operationName, out var metrics) ? metrics : null;
    }

    /// <summary>
    /// Reset all metrics
    /// </summary>
    public void Reset()
    {
        _metrics.Clear();
        _slowOperations.Clear();
    }

    /// <summary>
    /// Get formatted summary report
    /// </summary>
    public string GetSummaryReport()
    {
        var metrics = GetAllMetrics()
            .OrderByDescending(m => m.Value.TotalMilliseconds)
            .Take(20)
            .ToList();

        if (metrics.Count == 0)
            return "No performance metrics collected yet.";

        var report = "=== PERFORMANCE SUMMARY (Top 20 by Total Time) ===\n\n";

        foreach (var (name, metric) in metrics)
        {
            var successRate = metric.TotalCalls > 0
                ? ((metric.TotalCalls - metric.FailureCount) * 100.0 / metric.TotalCalls)
                : 0;

            report += $"""
                Operation: {name}
                  Calls: {metric.TotalCalls:N0}
                  Total Time: {metric.TotalMilliseconds:N0}ms
                  Average: {metric.AverageMilliseconds:F2}ms
                  Min: {metric.MinMilliseconds}ms | Max: {metric.MaxMilliseconds}ms
                  Success Rate: {successRate:F1}%

                """;
        }

        var slowOps = GetSlowOperations(TimeSpan.FromMinutes(5)).Take(10).ToList();
        if (slowOps.Count > 0)
        {
            report += "\n=== RECENT SLOW OPERATIONS (Last 5 minutes) ===\n\n";
            foreach (var op in slowOps)
            {
                report += $"[{op.Timestamp:HH:mm:ss}] {op.OperationName}: {op.DurationMs}ms\n";
            }
        }

        return report;
    }
}
