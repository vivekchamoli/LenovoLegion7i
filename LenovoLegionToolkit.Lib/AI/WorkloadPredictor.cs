using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Workload Predictor - Proactive workload classification and prediction
/// Enables zero-latency optimization by predicting workload changes before they occur
///
/// Purpose: Eliminate the 50-200ms lag between workload detection and optimization
/// </summary>
public class WorkloadPredictor
{
    private readonly CognitiveMemoryLayer _cognitiveMemory;
    private readonly List<WorkloadTransition> _transitionHistory = new();
    private const int MaxTransitionHistory = 500;

    public WorkloadPredictor(CognitiveMemoryLayer cognitiveMemory)
    {
        _cognitiveMemory = cognitiveMemory ?? throw new ArgumentNullException(nameof(cognitiveMemory));
    }

    /// <summary>
    /// Records a workload transition for pattern learning
    /// </summary>
    public void RecordTransition(WorkloadType from, WorkloadType to, TimeSpan duration)
    {
        lock (_transitionHistory)
        {
            _transitionHistory.Add(new WorkloadTransition
            {
                Timestamp = DateTime.Now,
                FromWorkload = from,
                ToWorkload = to,
                Duration = duration,
                DayOfWeek = DateTime.Now.DayOfWeek,
                Hour = DateTime.Now.Hour
            });

            // Prune old transitions
            if (_transitionHistory.Count > MaxTransitionHistory)
            {
                var toRemove = _transitionHistory.Count - MaxTransitionHistory;
                _transitionHistory.RemoveRange(0, toRemove);
            }
        }
    }

    /// <summary>
    /// Predicts the next likely workload based on current workload and time patterns
    /// </summary>
    public PredictedWorkload PredictNextWorkload(WorkloadType currentWorkload)
    {
        lock (_transitionHistory)
        {
            if (_transitionHistory.Count < 10)
            {
                // Insufficient data for prediction
                return new PredictedWorkload
                {
                    Workload = currentWorkload,
                    Confidence = 0.0,
                    TimeToTransition = null,
                    Reason = "Insufficient historical data"
                };
            }

            var now = DateTime.Now;

            // Find similar transitions (same current workload, similar time of day)
            var similarTransitions = _transitionHistory
                .Where(t => t.FromWorkload == currentWorkload)
                .Where(t => Math.Abs(t.Hour - now.Hour) <= 2) // Within 2 hours
                .ToList();

            if (similarTransitions.Count == 0)
            {
                // No similar patterns found
                return new PredictedWorkload
                {
                    Workload = currentWorkload,
                    Confidence = 0.0,
                    TimeToTransition = null,
                    Reason = "No similar patterns at this time of day"
                };
            }

            // Group by destination workload and count occurrences
            var transitionGroups = similarTransitions
                .GroupBy(t => t.ToWorkload)
                .Select(g => new
                {
                    Workload = g.Key,
                    Count = g.Count(),
                    AverageDuration = TimeSpan.FromTicks((long)g.Average(t => t.Duration.Ticks))
                })
                .OrderByDescending(g => g.Count)
                .ToList();

            var mostLikely = transitionGroups.First();
            var totalTransitions = transitionGroups.Sum(g => g.Count);
            var confidence = (double)mostLikely.Count / totalTransitions;

            // Calculate time to transition based on average duration
            var recentTransitions = _transitionHistory
                .Where(t => t.FromWorkload == currentWorkload && t.ToWorkload == mostLikely.Workload)
                .OrderByDescending(t => t.Timestamp)
                .Take(5)
                .ToList();

            TimeSpan? estimatedTimeToTransition = null;
            if (recentTransitions.Any())
            {
                estimatedTimeToTransition = TimeSpan.FromTicks(
                    (long)recentTransitions.Average(t => t.Duration.Ticks));
            }

            return new PredictedWorkload
            {
                Workload = mostLikely.Workload,
                Confidence = confidence,
                TimeToTransition = estimatedTimeToTransition,
                Reason = $"{mostLikely.Count}/{totalTransitions} similar transitions at this time"
            };
        }
    }

    /// <summary>
    /// Detects time-based patterns (e.g., gaming every weekday at 7pm)
    /// </summary>
    public List<TimeBasedWorkloadPattern> DetectTimeBasedPatterns()
    {
        lock (_transitionHistory)
        {
            var patterns = new List<TimeBasedWorkloadPattern>();

            // Group by day of week and hour
            var timeGroups = _transitionHistory
                .GroupBy(t => new { t.DayOfWeek, t.Hour, t.ToWorkload })
                .Where(g => g.Count() >= 3) // At least 3 occurrences
                .Select(g => new
                {
                    DayOfWeek = g.Key.DayOfWeek,
                    Hour = g.Key.Hour,
                    Workload = g.Key.ToWorkload,
                    Count = g.Count(),
                    Confidence = g.Count() / (double)_transitionHistory.Count(t => t.DayOfWeek == g.Key.DayOfWeek && t.Hour == g.Key.Hour)
                })
                .Where(g => g.Confidence >= 0.5) // At least 50% confidence
                .ToList();

            foreach (var group in timeGroups)
            {
                patterns.Add(new TimeBasedWorkloadPattern
                {
                    WorkloadType = group.Workload,
                    DayOfWeek = group.DayOfWeek,
                    Hour = group.Hour,
                    Confidence = group.Confidence,
                    Occurrences = group.Count
                });
            }

            return patterns;
        }
    }

    /// <summary>
    /// Checks if we're approaching a predicted workload transition time
    /// </summary>
    public bool IsApproachingTransition(WorkloadType currentWorkload, out PredictedWorkload prediction)
    {
        prediction = PredictNextWorkload(currentWorkload);

        if (prediction.Confidence < 0.6) // Require 60% confidence
            return false;

        if (prediction.TimeToTransition == null)
            return false;

        // Consider "approaching" if within 30 seconds of predicted transition
        return prediction.TimeToTransition.Value.TotalSeconds <= 30;
    }

    /// <summary>
    /// Gets diagnostic information about prediction accuracy
    /// </summary>
    public PredictionStatistics GetStatistics()
    {
        lock (_transitionHistory)
        {
            if (_transitionHistory.Count == 0)
            {
                return new PredictionStatistics
                {
                    TotalTransitions = 0,
                    MostCommonTransition = null,
                    AverageTransitionDuration = TimeSpan.Zero
                };
            }

            var mostCommon = _transitionHistory
                .GroupBy(t => new { t.FromWorkload, t.ToWorkload })
                .OrderByDescending(g => g.Count())
                .First();

            return new PredictionStatistics
            {
                TotalTransitions = _transitionHistory.Count,
                MostCommonTransition = $"{mostCommon.Key.FromWorkload} → {mostCommon.Key.ToWorkload}",
                AverageTransitionDuration = TimeSpan.FromTicks(
                    (long)_transitionHistory.Average(t => t.Duration.Ticks)),
                UniqueWorkloadTypes = _transitionHistory
                    .SelectMany(t => new[] { t.FromWorkload, t.ToWorkload })
                    .Distinct()
                    .Count()
            };
        }
    }
}

#region Data Models

/// <summary>
/// Represents a workload transition event
/// </summary>
public class WorkloadTransition
{
    public DateTime Timestamp { get; init; }
    public WorkloadType FromWorkload { get; init; }
    public WorkloadType ToWorkload { get; init; }
    public TimeSpan Duration { get; init; } // How long FromWorkload lasted
    public DayOfWeek DayOfWeek { get; init; }
    public int Hour { get; init; }
}

/// <summary>
/// Prediction result for next workload
/// </summary>
public class PredictedWorkload
{
    public WorkloadType Workload { get; init; }
    public double Confidence { get; init; } // 0.0 to 1.0
    public TimeSpan? TimeToTransition { get; init; }
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Time-based workload pattern (e.g., gaming every Friday at 7pm)
/// </summary>
public class TimeBasedWorkloadPattern
{
    public WorkloadType WorkloadType { get; init; }
    public DayOfWeek DayOfWeek { get; init; }
    public int Hour { get; init; }
    public double Confidence { get; init; }
    public int Occurrences { get; init; }
}

/// <summary>
/// Statistics about prediction system
/// </summary>
public class PredictionStatistics
{
    public int TotalTransitions { get; init; }
    public string? MostCommonTransition { get; init; }
    public TimeSpan AverageTransitionDuration { get; init; }
    public int UniqueWorkloadTypes { get; init; }
}

#endregion
