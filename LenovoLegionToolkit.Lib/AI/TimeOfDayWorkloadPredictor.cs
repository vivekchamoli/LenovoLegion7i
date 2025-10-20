using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Time-of-Day Workload Predictor
/// PHASE 2: ML-based prediction of workload based on time patterns
/// Predicts user activity patterns: Work hours, gaming time, media time, idle time
/// </summary>
public class TimeOfDayWorkloadPredictor
{
    private readonly WorkloadPatternLearner _patternLearner;
    private readonly Dictionary<string, TimeWindow> _predefinedPatterns;

    public TimeOfDayWorkloadPredictor(WorkloadPatternLearner patternLearner)
    {
        _patternLearner = patternLearner ?? throw new ArgumentNullException(nameof(patternLearner));
        _predefinedPatterns = InitializePredefinedPatterns();
    }

    /// <summary>
    /// Predict workload for current time with ML + heuristics
    /// </summary>
    public PredictionResult PredictCurrentWorkload()
    {
        return PredictWorkloadForTime(DateTime.Now);
    }

    /// <summary>
    /// Predict workload for specific time
    /// Combines ML predictions with time-of-day heuristics
    /// </summary>
    public PredictionResult PredictWorkloadForTime(DateTime targetTime)
    {
        // Try ML prediction first (learned patterns)
        var mlPrediction = _patternLearner.PredictWorkloadForTime(targetTime);

        if (mlPrediction.Confidence >= 0.7) // High confidence ML prediction
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ML prediction (high confidence): {mlPrediction.PredictedWorkload} ({mlPrediction.Confidence:P0})");

            return new PredictionResult
            {
                Workload = mlPrediction.PredictedWorkload,
                Confidence = mlPrediction.Confidence,
                Source = PredictionSource.MachineLearning,
                Reason = $"ML: {mlPrediction.Reason}"
            };
        }

        // Fallback to heuristic prediction (time-based patterns)
        var heuristicPrediction = PredictUsingHeuristics(targetTime);

        // If ML has medium confidence, blend with heuristics
        if (mlPrediction.Confidence >= 0.4 && mlPrediction.Confidence < 0.7)
        {
            // Weighted average of ML and heuristic confidence
            var blendedConfidence = (mlPrediction.Confidence * 0.6) + (heuristicPrediction.Confidence * 0.4);

            // Use ML prediction if confidence is decent
            if (mlPrediction.Confidence > heuristicPrediction.Confidence)
            {
                return new PredictionResult
                {
                    Workload = mlPrediction.PredictedWorkload,
                    Confidence = blendedConfidence,
                    Source = PredictionSource.Hybrid,
                    Reason = $"ML + Heuristic blend: {mlPrediction.Reason}"
                };
            }
        }

        // Use heuristic prediction
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Heuristic prediction: {heuristicPrediction.Workload} ({heuristicPrediction.Confidence:P0})");

        return heuristicPrediction;
    }

    /// <summary>
    /// Predict using time-of-day heuristics
    /// </summary>
    private PredictionResult PredictUsingHeuristics(DateTime targetTime)
    {
        var hour = targetTime.Hour;
        var dayOfWeek = targetTime.DayOfWeek;
        var isWeekend = dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday;

        // Early morning (5 AM - 8 AM): Light productivity or idle
        if (hour >= 5 && hour < 8)
        {
            return new PredictionResult
            {
                Workload = WorkloadType.LightProductivity,
                Confidence = 0.6,
                Source = PredictionSource.Heuristic,
                Reason = "Early morning hours"
            };
        }

        // Work hours (8 AM - 12 PM): Heavy productivity weekdays, light on weekends
        if (hour >= 8 && hour < 12)
        {
            return new PredictionResult
            {
                Workload = isWeekend ? WorkloadType.LightProductivity : WorkloadType.HeavyProductivity,
                Confidence = isWeekend ? 0.5 : 0.7,
                Source = PredictionSource.Heuristic,
                Reason = isWeekend ? "Weekend morning" : "Weekday work hours"
            };
        }

        // Lunch hours (12 PM - 1 PM): Media or light productivity
        if (hour >= 12 && hour < 13)
        {
            return new PredictionResult
            {
                Workload = WorkloadType.MediaPlayback,
                Confidence = 0.6,
                Source = PredictionSource.Heuristic,
                Reason = "Lunch hour (likely media/browsing)"
            };
        }

        // Afternoon work (1 PM - 5 PM): Heavy productivity weekdays
        if (hour >= 13 && hour < 17)
        {
            return new PredictionResult
            {
                Workload = isWeekend ? WorkloadType.LightProductivity : WorkloadType.HeavyProductivity,
                Confidence = isWeekend ? 0.5 : 0.7,
                Source = PredictionSource.Heuristic,
                Reason = isWeekend ? "Weekend afternoon" : "Weekday afternoon work"
            };
        }

        // Evening (5 PM - 8 PM): Transition time, light productivity or media
        if (hour >= 17 && hour < 20)
        {
            return new PredictionResult
            {
                Workload = WorkloadType.LightProductivity,
                Confidence = 0.5,
                Source = PredictionSource.Heuristic,
                Reason = "Evening transition period"
            };
        }

        // Prime time (8 PM - 11 PM): Gaming or media
        if (hour >= 20 && hour < 23)
        {
            return new PredictionResult
            {
                Workload = isWeekend ? WorkloadType.Gaming : WorkloadType.MediaPlayback,
                Confidence = 0.6,
                Source = PredictionSource.Heuristic,
                Reason = isWeekend ? "Weekend evening (gaming likely)" : "Weekday evening (media likely)"
            };
        }

        // Late night (11 PM - 1 AM): Gaming or media
        if (hour >= 23 || hour < 1)
        {
            return new PredictionResult
            {
                Workload = WorkloadType.Gaming,
                Confidence = 0.5,
                Source = PredictionSource.Heuristic,
                Reason = "Late night hours"
            };
        }

        // Very late night / early morning (1 AM - 5 AM): Idle or light activity
        if (hour >= 1 && hour < 5)
        {
            return new PredictionResult
            {
                Workload = WorkloadType.Idle,
                Confidence = 0.8,
                Source = PredictionSource.Heuristic,
                Reason = "Very late night / early morning"
            };
        }

        // Default: Light productivity
        return new PredictionResult
        {
            Workload = WorkloadType.LightProductivity,
            Confidence = 0.4,
            Source = PredictionSource.Heuristic,
            Reason = "Default prediction"
        };
    }

    /// <summary>
    /// Get prediction for next N hours
    /// </summary>
    public List<HourlyPrediction> PredictNextHours(int hours = 4)
    {
        var predictions = new List<HourlyPrediction>();
        var currentTime = DateTime.Now;

        for (int i = 0; i < hours; i++)
        {
            var targetTime = currentTime.AddHours(i);
            var prediction = PredictWorkloadForTime(targetTime);

            predictions.Add(new HourlyPrediction
            {
                Time = targetTime,
                Prediction = prediction
            });
        }

        return predictions;
    }

    /// <summary>
    /// Get optimal power profile for predicted workload
    /// </summary>
    public string GetOptimalPowerProfile(PredictionResult prediction)
    {
        return prediction.Workload switch
        {
            WorkloadType.Gaming => "Gaming",
            WorkloadType.MediaPlayback => "MediaPlayback",
            WorkloadType.Compilation => "Balanced",
            WorkloadType.AIWorkload => "Balanced",
            WorkloadType.VideoConferencing => "Balanced",
            WorkloadType.HeavyProductivity => "Balanced",
            WorkloadType.LightProductivity => "Quiet",
            WorkloadType.Idle => "BatterySaving",
            _ => "Balanced"
        };
    }

    /// <summary>
    /// Check if prediction is reliable enough for autonomous action
    /// </summary>
    public bool IsPredictionReliable(PredictionResult prediction, double minimumConfidence = 0.6)
    {
        return prediction.Confidence >= minimumConfidence;
    }

    /// <summary>
    /// Get time windows for specific workloads
    /// </summary>
    public List<TimeWindow> GetWorkloadTimeWindows(WorkloadType workload)
    {
        var stats = _patternLearner.GetStatisticsForTimeWindow(TimeSpan.Zero, TimeSpan.FromHours(24));

        if (!stats.WorkloadBreakdown.ContainsKey(workload))
            return new List<TimeWindow>();

        var workloadStats = stats.WorkloadBreakdown[workload];

        // Find peak hours for this workload
        var timeWindows = new List<TimeWindow>();

        if (workloadStats.TimeOfDayDistribution.Any())
        {
            var peakHours = workloadStats.TimeOfDayDistribution
                .OrderByDescending(kvp => kvp.Value)
                .Take(3)
                .Select(kvp => new TimeWindow
                {
                    StartHour = kvp.Key,
                    EndHour = kvp.Key + 1,
                    Workload = workload,
                    Frequency = (double)kvp.Value / stats.TotalOccurrences
                })
                .ToList();

            return peakHours;
        }

        return timeWindows;
    }

    /// <summary>
    /// Initialize predefined time-of-day patterns
    /// </summary>
    private Dictionary<string, TimeWindow> InitializePredefinedPatterns()
    {
        return new Dictionary<string, TimeWindow>
        {
            ["MorningWork"] = new TimeWindow { StartHour = 8, EndHour = 12, Workload = WorkloadType.HeavyProductivity },
            ["LunchBreak"] = new TimeWindow { StartHour = 12, EndHour = 13, Workload = WorkloadType.MediaPlayback },
            ["AfternoonWork"] = new TimeWindow { StartHour = 13, EndHour = 17, Workload = WorkloadType.HeavyProductivity },
            ["EveningRelax"] = new TimeWindow { StartHour = 18, EndHour = 20, Workload = WorkloadType.LightProductivity },
            ["PrimeTime"] = new TimeWindow { StartHour = 20, EndHour = 23, Workload = WorkloadType.Gaming },
            ["Sleep"] = new TimeWindow { StartHour = 1, EndHour = 6, Workload = WorkloadType.Idle }
        };
    }
}

/// <summary>
/// Prediction result with confidence and source
/// </summary>
public class PredictionResult
{
    public WorkloadType Workload { get; set; }
    public double Confidence { get; set; } // 0.0 - 1.0
    public PredictionSource Source { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>
/// Prediction source
/// </summary>
public enum PredictionSource
{
    MachineLearning,  // From learned patterns
    Heuristic,        // From time-of-day rules
    Hybrid            // Combined ML + Heuristic
}

/// <summary>
/// Hourly prediction
/// </summary>
public class HourlyPrediction
{
    public DateTime Time { get; set; }
    public PredictionResult Prediction { get; set; } = new();
}

/// <summary>
/// Time window for workload
/// </summary>
public class TimeWindow
{
    public int StartHour { get; set; }
    public int EndHour { get; set; }
    public WorkloadType Workload { get; set; }
    public double Frequency { get; set; }

    public override string ToString()
    {
        return $"{StartHour:D2}:00 - {EndHour:D2}:00 ({Workload})";
    }
}
