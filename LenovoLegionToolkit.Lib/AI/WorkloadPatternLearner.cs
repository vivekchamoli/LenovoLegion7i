using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Workload Pattern Learner - ML-based user behavior analysis
/// PHASE 2: Learns user patterns to predict future workloads
/// Tracks: Workload types, time-of-day patterns, duration, frequency
/// </summary>
public class WorkloadPatternLearner
{
    private readonly string _patternDataPath;
    private WorkloadPatternData _patterns;
    private readonly object _lock = new();

    private const int MaxHistoryDays = 30; // Keep 30 days of history
    private const int MinSamplesForPrediction = 10; // Need 10 samples for reliable prediction

    public WorkloadPatternLearner()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LenovoLegionToolkit",
            "AI"
        );

        Directory.CreateDirectory(appDataPath);
        _patternDataPath = Path.Combine(appDataPath, "workload_patterns.json");

        _patterns = LoadPatterns();
    }

    /// <summary>
    /// Record workload occurrence for learning
    /// </summary>
    public void RecordWorkload(WorkloadType workload, DateTime timestamp, int durationSeconds, string? processName = null)
    {
        lock (_lock)
        {
            var occurrence = new WorkloadOccurrence
            {
                Workload = workload,
                Timestamp = timestamp,
                DurationSeconds = durationSeconds,
                ProcessName = processName,
                TimeOfDay = timestamp.TimeOfDay,
                DayOfWeek = timestamp.DayOfWeek
            };

            _patterns.Occurrences.Add(occurrence);

            // Update statistics
            UpdateStatistics(workload, timestamp);

            // Prune old data (keep last 30 days)
            PruneOldData();

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Recorded workload: {workload} at {timestamp:HH:mm}, duration: {durationSeconds}s");
        }
    }

    /// <summary>
    /// Predict workload for a specific time
    /// </summary>
    public PatternPredictionResult PredictWorkloadForTime(DateTime targetTime)
    {
        lock (_lock)
        {
            var timeOfDay = targetTime.TimeOfDay;
            var dayOfWeek = targetTime.DayOfWeek;

            // Find similar time patterns (within 30-minute window)
            var similarOccurrences = _patterns.Occurrences
                .Where(o => Math.Abs((o.TimeOfDay - timeOfDay).TotalMinutes) <= 30)
                .Where(o => o.DayOfWeek == dayOfWeek || IsWeekdaySimilar(o.DayOfWeek, dayOfWeek))
                .ToList();

            if (similarOccurrences.Count < MinSamplesForPrediction)
            {
                return new PatternPredictionResult
                {
                    PredictedWorkload = WorkloadType.Unknown,
                    Confidence = 0.0,
                    Reason = $"Insufficient data ({similarOccurrences.Count}/{MinSamplesForPrediction} samples)"
                };
            }

            // Calculate workload frequency
            var workloadGroups = similarOccurrences
                .GroupBy(o => o.Workload)
                .Select(g => new
                {
                    Workload = g.Key,
                    Count = g.Count(),
                    Frequency = (double)g.Count() / similarOccurrences.Count
                })
                .OrderByDescending(x => x.Frequency)
                .ToList();

            var mostCommon = workloadGroups.First();

            // Calculate confidence based on:
            // 1. Frequency (how often this workload occurs at this time)
            // 2. Sample size (more samples = higher confidence)
            // 3. Recency (recent patterns weighted higher)
            var frequencyScore = mostCommon.Frequency; // 0.0 - 1.0
            var sampleScore = Math.Min(1.0, similarOccurrences.Count / 50.0); // Plateau at 50 samples
            var recencyScore = CalculateRecencyScore(similarOccurrences.Where(o => o.Workload == mostCommon.Workload).ToList());

            var confidence = (frequencyScore * 0.5) + (sampleScore * 0.3) + (recencyScore * 0.2);

            return new PatternPredictionResult
            {
                PredictedWorkload = mostCommon.Workload,
                Confidence = confidence,
                SampleCount = similarOccurrences.Count,
                Frequency = mostCommon.Frequency,
                Reason = $"{mostCommon.Frequency:P0} of {similarOccurrences.Count} samples"
            };
        }
    }

    /// <summary>
    /// Get workload statistics for a time window
    /// </summary>
    public WorkloadStatistics GetStatisticsForTimeWindow(TimeSpan startTime, TimeSpan endTime)
    {
        lock (_lock)
        {
            var occurrences = _patterns.Occurrences
                .Where(o => o.TimeOfDay >= startTime && o.TimeOfDay <= endTime)
                .ToList();

            var stats = new WorkloadStatistics
            {
                TimeWindow = $"{startTime:hh\\:mm} - {endTime:hh\\:mm}",
                TotalOccurrences = occurrences.Count,
                WorkloadBreakdown = occurrences
                    .GroupBy(o => o.Workload)
                    .ToDictionary(
                        g => g.Key,
                        g => new WorkloadStats
                        {
                            Count = g.Count(),
                            Frequency = (double)g.Count() / occurrences.Count,
                            AverageDurationSeconds = (int)g.Average(o => o.DurationSeconds),
                            TotalDurationSeconds = g.Sum(o => o.DurationSeconds)
                        }
                    )
            };

            return stats;
        }
    }

    /// <summary>
    /// Get top workload patterns
    /// </summary>
    public List<WorkloadPattern> GetTopPatterns(int topN = 5)
    {
        lock (_lock)
        {
            var patterns = new List<WorkloadPattern>();

            // Group by time-of-day bins (1-hour bins)
            var timeBins = _patterns.Occurrences
                .GroupBy(o => new
                {
                    Hour = o.Timestamp.Hour,
                    Workload = o.Workload
                })
                .Select(g => new
                {
                    Hour = g.Key.Hour,
                    Workload = g.Key.Workload,
                    Count = g.Count(),
                    AvgDuration = (int)g.Average(o => o.DurationSeconds)
                })
                .OrderByDescending(x => x.Count)
                .Take(topN)
                .ToList();

            foreach (var bin in timeBins)
            {
                patterns.Add(new WorkloadPattern
                {
                    Workload = bin.Workload,
                    TimeWindow = $"{bin.Hour:D2}:00 - {bin.Hour + 1:D2}:00",
                    Frequency = bin.Count,
                    AverageDurationSeconds = bin.AvgDuration
                });
            }

            return patterns;
        }
    }

    /// <summary>
    /// Save patterns to disk
    /// </summary>
    public async Task SavePatternsAsync()
    {
        try
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(_patterns, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_patternDataPath, json);
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Workload patterns saved: {_patterns.Occurrences.Count} occurrences");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to save workload patterns", ex);
        }
    }

    /// <summary>
    /// Load patterns from disk
    /// </summary>
    private WorkloadPatternData LoadPatterns()
    {
        try
        {
            if (File.Exists(_patternDataPath))
            {
                var json = File.ReadAllText(_patternDataPath);
                var patterns = JsonSerializer.Deserialize<WorkloadPatternData>(json);

                if (patterns != null)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Loaded workload patterns: {patterns.Occurrences.Count} occurrences");
                    return patterns;
                }
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to load workload patterns", ex);
        }

        return new WorkloadPatternData();
    }

    /// <summary>
    /// Update statistics for a workload
    /// </summary>
    private void UpdateStatistics(WorkloadType workload, DateTime timestamp)
    {
        if (!_patterns.Statistics.ContainsKey(workload))
        {
            _patterns.Statistics[workload] = new WorkloadStats();
        }

        var stats = _patterns.Statistics[workload];
        stats.Count++;
        stats.LastOccurrence = timestamp;

        // Update time-of-day distribution
        var hour = timestamp.Hour;
        if (!stats.TimeOfDayDistribution.ContainsKey(hour))
        {
            stats.TimeOfDayDistribution[hour] = 0;
        }
        stats.TimeOfDayDistribution[hour]++;
    }

    /// <summary>
    /// Prune old occurrences (keep last 30 days)
    /// </summary>
    private void PruneOldData()
    {
        var cutoffDate = DateTime.Now.AddDays(-MaxHistoryDays);
        var originalCount = _patterns.Occurrences.Count;

        _patterns.Occurrences.RemoveAll(o => o.Timestamp < cutoffDate);

        if (originalCount != _patterns.Occurrences.Count && Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Pruned old workload data: {originalCount - _patterns.Occurrences.Count} occurrences removed");
        }
    }

    /// <summary>
    /// Calculate recency score (recent patterns weighted higher)
    /// </summary>
    private double CalculateRecencyScore(List<WorkloadOccurrence> occurrences)
    {
        if (occurrences.Count == 0)
            return 0.0;

        var now = DateTime.Now;
        var recencyScores = occurrences.Select(o =>
        {
            var ageInDays = (now - o.Timestamp).TotalDays;
            // Exponential decay: score = e^(-age/10)
            // Recent (0 days): 1.0, 7 days: 0.5, 14 days: 0.25
            return Math.Exp(-ageInDays / 10.0);
        }).ToList();

        return recencyScores.Average();
    }

    /// <summary>
    /// Check if weekdays are similar (Mon-Fri similar, Sat-Sun similar)
    /// </summary>
    private bool IsWeekdaySimilar(DayOfWeek day1, DayOfWeek day2)
    {
        var isDay1Weekday = day1 != DayOfWeek.Saturday && day1 != DayOfWeek.Sunday;
        var isDay2Weekday = day2 != DayOfWeek.Saturday && day2 != DayOfWeek.Sunday;

        return isDay1Weekday == isDay2Weekday;
    }

    /// <summary>
    /// Get learning progress summary
    /// </summary>
    public LearningProgress GetLearningProgress()
    {
        lock (_lock)
        {
            var totalOccurrences = _patterns.Occurrences.Count;
            var uniqueDays = _patterns.Occurrences
                .Select(o => o.Timestamp.Date)
                .Distinct()
                .Count();

            var workloadCoverage = _patterns.Statistics.Count(kvp => kvp.Value.Count >= MinSamplesForPrediction);

            return new LearningProgress
            {
                TotalOccurrences = totalOccurrences,
                UniqueDays = uniqueDays,
                WorkloadsCovered = workloadCoverage,
                TotalWorkloadTypes = Enum.GetValues<WorkloadType>().Length,
                IsReady = totalOccurrences >= 50 && uniqueDays >= 3,
                DataQuality = CalculateDataQuality()
            };
        }
    }

    /// <summary>
    /// Calculate data quality score (0.0 - 1.0)
    /// </summary>
    private double CalculateDataQuality()
    {
        var totalOccurrences = _patterns.Occurrences.Count;
        if (totalOccurrences == 0)
            return 0.0;

        // Quality factors:
        // 1. Diversity (multiple workload types)
        var uniqueWorkloads = _patterns.Occurrences.Select(o => o.Workload).Distinct().Count();
        var diversityScore = Math.Min(1.0, uniqueWorkloads / 5.0); // 5 workload types = full diversity

        // 2. Coverage (multiple days)
        var uniqueDays = _patterns.Occurrences.Select(o => o.Timestamp.Date).Distinct().Count();
        var coverageScore = Math.Min(1.0, uniqueDays / 7.0); // 7 days = full coverage

        // 3. Volume (sufficient samples)
        var volumeScore = Math.Min(1.0, totalOccurrences / 100.0); // 100 samples = full volume

        return (diversityScore * 0.4) + (coverageScore * 0.4) + (volumeScore * 0.2);
    }
}

/// <summary>
/// Workload pattern data (persisted)
/// </summary>
public class WorkloadPatternData
{
    public List<WorkloadOccurrence> Occurrences { get; set; } = new();
    public Dictionary<WorkloadType, WorkloadStats> Statistics { get; set; } = new();
}

/// <summary>
/// Single workload occurrence
/// </summary>
public class WorkloadOccurrence
{
    public WorkloadType Workload { get; set; }
    public DateTime Timestamp { get; set; }
    public int DurationSeconds { get; set; }
    public string? ProcessName { get; set; }
    public TimeSpan TimeOfDay { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
}

/// <summary>
/// Workload statistics
/// </summary>
public class WorkloadStats
{
    public int Count { get; set; }
    public double Frequency { get; set; }
    public int AverageDurationSeconds { get; set; }
    public int TotalDurationSeconds { get; set; }
    public DateTime? LastOccurrence { get; set; }
    public Dictionary<int, int> TimeOfDayDistribution { get; set; } = new(); // Hour -> Count
}

/// <summary>
/// Pattern prediction result from ML learning
/// </summary>
public class PatternPredictionResult
{
    public WorkloadType PredictedWorkload { get; set; }
    public double Confidence { get; set; } // 0.0 - 1.0
    public int SampleCount { get; set; }
    public double Frequency { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>
/// Workload statistics for time window
/// </summary>
public class WorkloadStatistics
{
    public string TimeWindow { get; set; } = "";
    public int TotalOccurrences { get; set; }
    public Dictionary<WorkloadType, WorkloadStats> WorkloadBreakdown { get; set; } = new();
}

/// <summary>
/// Workload pattern
/// </summary>
public class WorkloadPattern
{
    public WorkloadType Workload { get; set; }
    public string TimeWindow { get; set; } = "";
    public int Frequency { get; set; }
    public int AverageDurationSeconds { get; set; }
}

/// <summary>
/// Learning progress status
/// </summary>
public class LearningProgress
{
    public int TotalOccurrences { get; set; }
    public int UniqueDays { get; set; }
    public int WorkloadsCovered { get; set; }
    public int TotalWorkloadTypes { get; set; }
    public bool IsReady { get; set; }
    public double DataQuality { get; set; } // 0.0 - 1.0

    public string GetSummary()
    {
        return $@"Learning Progress:
- Occurrences: {TotalOccurrences}
- Unique Days: {UniqueDays}
- Workloads Covered: {WorkloadsCovered}/{TotalWorkloadTypes}
- Data Quality: {DataQuality:P0}
- Ready for Predictions: {(IsReady ? "Yes ✅" : "No (need more data)")}";
    }
}
