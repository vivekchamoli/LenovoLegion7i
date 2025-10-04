using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// User Behavior Analyzer - Learns and predicts user patterns
/// Tracks historical behavior to make intelligent predictions
/// </summary>
public class UserBehaviorAnalyzer
{
    private readonly List<BehaviorDataPoint> _history = new();
    private const int MaxHistorySize = 10000; // ~2 weeks at 500ms intervals
    private readonly object _lock = new();

    /// <summary>
    /// Record a behavior data point
    /// </summary>
    public void RecordBehavior(SystemContext context, List<ResourceAction> executedActions)
    {
        lock (_lock)
        {
            var dataPoint = new BehaviorDataPoint
            {
                Timestamp = DateTime.Now,
                HourOfDay = DateTime.Now.Hour,
                DayOfWeek = DateTime.Now.DayOfWeek,
                IsOnBattery = context.BatteryState.IsOnBattery,
                BatteryPercent = context.BatteryState.ChargePercent,
                UserIntent = context.UserIntent,
                WorkloadType = context.CurrentWorkload.Type,
                CpuTemp = context.ThermalState.CpuTemp,
                GpuTemp = context.ThermalState.GpuTemp,
                ActionsExecuted = executedActions.Count
            };

            _history.Add(dataPoint);

            // Trim history if too large
            while (_history.Count > MaxHistorySize)
                _history.RemoveAt(0);

            if (Log.Instance.IsTraceEnabled && _history.Count % 1000 == 0)
                Log.Instance.Trace($"Behavior history: {_history.Count} data points");
        }
    }

    /// <summary>
    /// Predict when user typically unplugs from AC power
    /// </summary>
    public UnplugPrediction PredictUnplugTime()
    {
        lock (_lock)
        {
            if (_history.Count < 100)
            {
                return new UnplugPrediction
                {
                    Confidence = 0.0,
                    PredictedTime = DateTime.Now.AddHours(2),
                    Reason = "Insufficient historical data"
                };
            }

            var currentHour = DateTime.Now.Hour;
            var currentDay = DateTime.Now.DayOfWeek;

            // Find patterns of unplugging at similar times
            var unplugEvents = _history
                .Where(h => h.IsOnBattery && _history.IndexOf(h) > 0)
                .Where(h => !_history[_history.IndexOf(h) - 1].IsOnBattery)
                .ToList();

            if (unplugEvents.Count < 5)
            {
                return new UnplugPrediction
                {
                    Confidence = 0.0,
                    PredictedTime = DateTime.Now.AddHours(3),
                    Reason = "Insufficient unplug events"
                };
            }

            // Group by hour and day
            var hourlyPattern = unplugEvents
                .GroupBy(e => new { e.HourOfDay, e.DayOfWeek })
                .OrderByDescending(g => g.Count())
                .First();

            var nextUnplugHour = hourlyPattern.Key.HourOfDay;
            var confidence = hourlyPattern.Count() / (double)unplugEvents.Count;

            // Calculate next occurrence
            var predictedTime = DateTime.Now;
            while (predictedTime.Hour != nextUnplugHour || predictedTime.DayOfWeek != hourlyPattern.Key.DayOfWeek)
            {
                predictedTime = predictedTime.AddHours(1);
            }

            return new UnplugPrediction
            {
                Confidence = Math.Min(0.9, confidence),
                PredictedTime = predictedTime,
                Reason = $"User typically unplugs around {nextUnplugHour}:00 on {hourlyPattern.Key.DayOfWeek}s"
            };
        }
    }

    /// <summary>
    /// Predict typical workload at a given time
    /// </summary>
    public WorkloadPrediction PredictWorkloadAt(DateTime time)
    {
        lock (_lock)
        {
            if (_history.Count < 100)
            {
                return new WorkloadPrediction
                {
                    Confidence = 0.0,
                    PredictedWorkload = WorkloadType.Unknown,
                    Reason = "Insufficient historical data"
                };
            }

            var hour = time.Hour;
            var day = time.DayOfWeek;

            // Find similar time periods
            var similarTimes = _history
                .Where(h => h.HourOfDay == hour && h.DayOfWeek == day)
                .ToList();

            if (similarTimes.Count < 5)
            {
                return new WorkloadPrediction
                {
                    Confidence = 0.0,
                    PredictedWorkload = WorkloadType.Unknown,
                    Reason = "Insufficient data for this time period"
                };
            }

            // Most common workload at this time
            var workloadGroups = similarTimes
                .GroupBy(h => h.WorkloadType)
                .OrderByDescending(g => g.Count())
                .ToList();

            var mostCommon = workloadGroups.First();
            var confidence = mostCommon.Count() / (double)similarTimes.Count;

            return new WorkloadPrediction
            {
                Confidence = confidence,
                PredictedWorkload = mostCommon.Key,
                Reason = $"{confidence * 100:F0}% of the time at {hour}:00 on {day}s"
            };
        }
    }

    /// <summary>
    /// Analyze typical battery usage patterns
    /// </summary>
    public BatteryUsagePattern AnalyzeBatteryUsage()
    {
        lock (_lock)
        {
            var batteryEvents = _history.Where(h => h.IsOnBattery).ToList();

            if (batteryEvents.Count < 50)
            {
                return new BatteryUsagePattern
                {
                    AverageBatterySessionMinutes = 120,
                    TypicalDischargeRatePercentPerHour = 25,
                    MostCommonBatteryWorkload = WorkloadType.Unknown
                };
            }

            // Calculate average session length
            var sessions = new List<int>();
            var sessionStart = 0;
            for (int i = 1; i < _history.Count; i++)
            {
                if (_history[i].IsOnBattery && !_history[i - 1].IsOnBattery)
                {
                    sessionStart = i;
                }
                else if (!_history[i].IsOnBattery && _history[i - 1].IsOnBattery)
                {
                    sessions.Add(i - sessionStart);
                }
            }

            var avgSessionLength = sessions.Any() ? sessions.Average() : 240; // ~2 hours default
            var avgSessionMinutes = (avgSessionLength * 0.5); // 500ms intervals

            // Calculate discharge rate
            var dischargeRates = new List<double>();
            for (int i = 1; i < batteryEvents.Count; i++)
            {
                var timeDiff = (batteryEvents[i].Timestamp - batteryEvents[i - 1].Timestamp).TotalHours;
                if (timeDiff > 0 && timeDiff < 1)
                {
                    var batteryDiff = batteryEvents[i - 1].BatteryPercent - batteryEvents[i].BatteryPercent;
                    if (batteryDiff > 0)
                    {
                        dischargeRates.Add(batteryDiff / timeDiff);
                    }
                }
            }

            var avgDischargeRate = dischargeRates.Any() ? dischargeRates.Average() : 25;

            // Most common workload on battery
            var workloadGroups = batteryEvents
                .GroupBy(e => e.WorkloadType)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            return new BatteryUsagePattern
            {
                AverageBatterySessionMinutes = avgSessionMinutes,
                TypicalDischargeRatePercentPerHour = avgDischargeRate,
                MostCommonBatteryWorkload = workloadGroups?.Key ?? WorkloadType.Unknown
            };
        }
    }

    /// <summary>
    /// Get usage statistics for diagnostics
    /// </summary>
    public string GetStatistics()
    {
        lock (_lock)
        {
            if (_history.Count == 0)
                return "No behavior data collected yet";

            var batteryEvents = _history.Count(h => h.IsOnBattery);
            var acEvents = _history.Count - batteryEvents;
            var batteryPercent = (batteryEvents * 100.0) / _history.Count;

            var workloadGroups = _history
                .GroupBy(h => h.WorkloadType)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .ToList();

            return $"""
                Behavior Analysis Statistics:
                - Total data points: {_history.Count:N0}
                - Time span: {(_history.Last().Timestamp - _history.First().Timestamp).TotalDays:F1} days
                - Battery usage: {batteryPercent:F1}% of time
                - AC usage: {(100 - batteryPercent):F1}% of time

                Top workloads:
                {string.Join("\n", workloadGroups.Select((g, i) => $"  {i + 1}. {g.Key}: {(g.Count() * 100.0 / _history.Count):F1}%"))}
                """;
        }
    }

    /// <summary>
    /// Export behavior history for persistence
    /// </summary>
    public List<BehaviorDataPoint> GetHistory()
    {
        lock (_lock)
        {
            return new List<BehaviorDataPoint>(_history);
        }
    }

    /// <summary>
    /// Import behavior history from persistence
    /// </summary>
    public void LoadHistory(List<BehaviorDataPoint> history)
    {
        lock (_lock)
        {
            _history.Clear();
            _history.AddRange(history.Take(MaxHistorySize));

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Loaded {_history.Count} behavior data points from persistence");
        }
    }

    /// <summary>
    /// Get number of data points in history
    /// </summary>
    public int GetDataPointCount()
    {
        lock (_lock)
        {
            return _history.Count;
        }
    }
}

/// <summary>
/// Single behavior data point
/// </summary>
public class BehaviorDataPoint
{
    public DateTime Timestamp { get; set; }
    public int HourOfDay { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsOnBattery { get; set; }
    public int BatteryPercent { get; set; }
    public UserIntent UserIntent { get; set; }
    public WorkloadType WorkloadType { get; set; }
    public byte CpuTemp { get; set; }
    public byte GpuTemp { get; set; }
    public int ActionsExecuted { get; set; }
}

/// <summary>
/// Prediction of when user will unplug
/// </summary>
public class UnplugPrediction
{
    public double Confidence { get; set; }
    public DateTime PredictedTime { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Prediction of workload at a time
/// </summary>
public class WorkloadPrediction
{
    public double Confidence { get; set; }
    public WorkloadType PredictedWorkload { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Battery usage pattern analysis
/// </summary>
public class BatteryUsagePattern
{
    public double AverageBatterySessionMinutes { get; set; }
    public double TypicalDischargeRatePercentPerHour { get; set; }
    public WorkloadType MostCommonBatteryWorkload { get; set; }
}
