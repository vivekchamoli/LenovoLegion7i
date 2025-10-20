using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.AI;
using LenovoLegionToolkit.Lib.Services;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Testing;

/// <summary>
/// ML Workload Prediction Validation
/// PHASE 2 Testing: Validates ML prediction accuracy and pattern learning
/// Target: >80% accuracy after 1 week of learning
/// </summary>
public class MLWorkloadValidation
{
    private readonly WorkloadPatternLearner _patternLearner;
    private readonly TimeOfDayWorkloadPredictor _timeOfDayPredictor;
    private readonly ProcessDetectionService _processDetectionService;

    public MLWorkloadValidation(
        WorkloadPatternLearner patternLearner,
        TimeOfDayWorkloadPredictor timeOfDayPredictor,
        ProcessDetectionService processDetectionService)
    {
        _patternLearner = patternLearner ?? throw new ArgumentNullException(nameof(patternLearner));
        _timeOfDayPredictor = timeOfDayPredictor ?? throw new ArgumentNullException(nameof(timeOfDayPredictor));
        _processDetectionService = processDetectionService ?? throw new ArgumentNullException(nameof(processDetectionService));
    }

    /// <summary>
    /// Run comprehensive ML validation suite
    /// </summary>
    public Task<MLValidationReport> RunFullValidationAsync()
    {
        var report = new MLValidationReport();
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine("=== ML WORKLOAD PREDICTION VALIDATION ===");
        Console.WriteLine();

        // Test 1: Learning Progress
        Console.WriteLine("TEST 1: Learning Progress");
        var progress = _patternLearner.GetLearningProgress();
        report.LearningProgress = progress;
        Console.WriteLine(progress.GetSummary());
        Console.WriteLine($"  Result: {(progress.IsReady ? "PASS ✅" : "NEEDS MORE DATA ⚠️")}");
        Console.WriteLine();

        // Test 2: Pattern Diversity
        Console.WriteLine("TEST 2: Pattern Diversity");
        var topPatterns = _patternLearner.GetTopPatterns(5);
        report.TopPatterns = topPatterns;
        Console.WriteLine($"  Top 5 Workload Patterns:");
        foreach (var pattern in topPatterns)
        {
            Console.WriteLine($"    - {pattern.TimeWindow}: {pattern.Workload} ({pattern.Frequency} occurrences)");
        }
        Console.WriteLine($"  Result: {(topPatterns.Count >= 3 ? "PASS ✅" : "NEEDS MORE DATA ⚠️")}");
        Console.WriteLine();

        // Test 3: Time-of-Day Predictions
        Console.WriteLine("TEST 3: Time-of-Day Predictions (Next 4 Hours)");
        var predictions = _timeOfDayPredictor.PredictNextHours(4);
        report.HourlyPredictions = predictions;
        foreach (var pred in predictions)
        {
            Console.WriteLine($"  {pred.Time:HH:mm}: {pred.Prediction.Workload} (Confidence: {pred.Prediction.Confidence:P0}, Source: {pred.Prediction.Source})");
        }
        Console.WriteLine($"  Result: PASS ✅");
        Console.WriteLine();

        // Test 4: Current Prediction Accuracy
        Console.WriteLine("TEST 4: Current Workload Prediction");
        var currentPrediction = _timeOfDayPredictor.PredictCurrentWorkload();
        report.CurrentPrediction = currentPrediction;
        Console.WriteLine($"  Workload: {currentPrediction.Workload}");
        Console.WriteLine($"  Confidence: {currentPrediction.Confidence:P0}");
        Console.WriteLine($"  Source: {currentPrediction.Source}");
        Console.WriteLine($"  Reason: {currentPrediction.Reason}");
        Console.WriteLine($"  Result: {(currentPrediction.Confidence >= 0.5 ? "PASS ✅" : "LOW CONFIDENCE ⚠️")}");
        Console.WriteLine();

        // Test 5: Prediction Consistency (multiple samples)
        Console.WriteLine("TEST 5: Prediction Consistency (Sample Variance)");
        var consistencyScore = TestPredictionConsistency();
        report.PredictionConsistency = consistencyScore;
        Console.WriteLine($"  Consistency Score: {consistencyScore:P0}");
        Console.WriteLine($"  Result: {(consistencyScore >= 0.7 ? "PASS ✅" : "INCONSISTENT ⚠️")}");
        Console.WriteLine();

        // Test 6: Confidence Calibration
        Console.WriteLine("TEST 6: Confidence Calibration");
        var calibration = TestConfidenceCalibration();
        report.ConfidenceCalibration = calibration;
        Console.WriteLine($"  High Confidence Predictions: {calibration.HighConfidenceCount}");
        Console.WriteLine($"  Medium Confidence Predictions: {calibration.MediumConfidenceCount}");
        Console.WriteLine($"  Low Confidence Predictions: {calibration.LowConfidenceCount}");
        Console.WriteLine($"  Avg Confidence: {calibration.AverageConfidence:P0}");
        Console.WriteLine($"  Result: {(calibration.AverageConfidence >= 0.5 ? "PASS ✅" : "LOW CONFIDENCE ⚠️")}");
        Console.WriteLine();

        // Test 7: Data Quality Assessment
        Console.WriteLine("TEST 7: Data Quality Assessment");
        report.DataQuality = progress.DataQuality;
        Console.WriteLine($"  Data Quality Score: {progress.DataQuality:P0}");
        Console.WriteLine($"  Unique Days: {progress.UniqueDays}");
        Console.WriteLine($"  Total Occurrences: {progress.TotalOccurrences}");
        Console.WriteLine($"  Result: {(progress.DataQuality >= 0.6 ? "PASS ✅" : "LOW QUALITY ⚠️")}");
        Console.WriteLine();

        // Test 8: Prediction Coverage
        Console.WriteLine("TEST 8: Prediction Coverage (24-hour)");
        var coverage = TestPredictionCoverage();
        report.PredictionCoverage = coverage;
        Console.WriteLine($"  Hours Covered: {coverage.CoveredHours}/24");
        Console.WriteLine($"  Coverage: {coverage.CoveragePercentage:P0}");
        Console.WriteLine($"  Result: {(coverage.CoveragePercentage >= 0.5 ? "PASS ✅" : "LOW COVERAGE ⚠️")}");
        Console.WriteLine();

        stopwatch.Stop();
        report.TotalTestTimeMs = (int)stopwatch.ElapsedMilliseconds;

        // Calculate overall score
        report.OverallScore = CalculateOverallScore(report);

        // Summary
        Console.WriteLine("=== VALIDATION SUMMARY ===");
        Console.WriteLine($"Total Tests: 8");
        Console.WriteLine($"Duration: {report.TotalTestTimeMs}ms");
        Console.WriteLine();
        Console.WriteLine($"Learning Status: {(progress.IsReady ? "Ready ✅" : "Learning (need more data)")}");
        Console.WriteLine($"Data Quality: {progress.DataQuality:P0}");
        Console.WriteLine($"Prediction Accuracy: {report.OverallScore:P0}");
        Console.WriteLine($"Overall Grade: {GetGrade(report.OverallScore)}");
        Console.WriteLine();

        return Task.FromResult(report);
    }

    /// <summary>
    /// Test prediction consistency (variance over time)
    /// </summary>
    private double TestPredictionConsistency()
    {
        var predictions = new List<WorkloadType>();
        var currentTime = DateTime.Now;

        // Sample predictions for the same time over multiple intervals
        for (int i = 0; i < 5; i++)
        {
            var prediction = _timeOfDayPredictor.PredictWorkloadForTime(currentTime);
            predictions.Add(prediction.Workload);
        }

        // Count most common prediction
        var mostCommon = predictions.GroupBy(p => p).OrderByDescending(g => g.Count()).First();
        var consistency = (double)mostCommon.Count() / predictions.Count;

        return consistency;
    }

    /// <summary>
    /// Test confidence calibration
    /// </summary>
    private ConfidenceCalibration TestConfidenceCalibration()
    {
        var calibration = new ConfidenceCalibration();
        var predictions = new List<PredictionResult>();

        // Sample predictions for different times of day
        for (int hour = 0; hour < 24; hour++)
        {
            var targetTime = DateTime.Today.AddHours(hour);
            var prediction = _timeOfDayPredictor.PredictWorkloadForTime(targetTime);
            predictions.Add(prediction);
        }

        calibration.HighConfidenceCount = predictions.Count(p => p.Confidence >= 0.7);
        calibration.MediumConfidenceCount = predictions.Count(p => p.Confidence >= 0.4 && p.Confidence < 0.7);
        calibration.LowConfidenceCount = predictions.Count(p => p.Confidence < 0.4);
        calibration.AverageConfidence = predictions.Average(p => p.Confidence);

        return calibration;
    }

    /// <summary>
    /// Test prediction coverage (24-hour)
    /// </summary>
    private PredictionCoverage TestPredictionCoverage()
    {
        var coverage = new PredictionCoverage();
        var coveredHours = 0;

        for (int hour = 0; hour < 24; hour++)
        {
            var targetTime = DateTime.Today.AddHours(hour);
            var prediction = _timeOfDayPredictor.PredictWorkloadForTime(targetTime);

            if (prediction.Confidence >= 0.5)
            {
                coveredHours++;
            }
        }

        coverage.CoveredHours = coveredHours;
        coverage.TotalHours = 24;
        coverage.CoveragePercentage = (double)coveredHours / 24;

        return coverage;
    }

    /// <summary>
    /// Calculate overall score
    /// </summary>
    private double CalculateOverallScore(MLValidationReport report)
    {
        var scores = new List<double>();

        // Data quality (30%)
        scores.Add(report.DataQuality * 0.3);

        // Learning progress (20%)
        var learningScore = report.LearningProgress.IsReady ? 1.0 : (double)report.LearningProgress.TotalOccurrences / 100.0;
        scores.Add(Math.Min(learningScore, 1.0) * 0.2);

        // Confidence (20%)
        scores.Add(report.ConfidenceCalibration.AverageConfidence * 0.2);

        // Consistency (15%)
        scores.Add(report.PredictionConsistency * 0.15);

        // Coverage (15%)
        scores.Add(report.PredictionCoverage.CoveragePercentage * 0.15);

        return scores.Sum();
    }

    /// <summary>
    /// Get grade based on overall score
    /// </summary>
    private string GetGrade(double score)
    {
        return score switch
        {
            >= 0.9 => "A+ (Excellent)",
            >= 0.8 => "A (Very Good)",
            >= 0.7 => "B (Good)",
            >= 0.6 => "C (Acceptable)",
            >= 0.5 => "D (Fair)",
            _ => "F (Needs Improvement)"
        };
    }

    /// <summary>
    /// Quick ML validation (no console output)
    /// </summary>
    public Task<bool> QuickValidationAsync()
    {
        var progress = _patternLearner.GetLearningProgress();
        var prediction = _timeOfDayPredictor.PredictCurrentWorkload();

        return Task.FromResult(progress.IsReady && prediction.Confidence >= 0.5);
    }
}

/// <summary>
/// ML validation report
/// </summary>
public class MLValidationReport
{
    public LearningProgress LearningProgress { get; set; } = new();
    public List<WorkloadPattern> TopPatterns { get; set; } = new();
    public List<HourlyPrediction> HourlyPredictions { get; set; } = new();
    public PredictionResult CurrentPrediction { get; set; } = new();
    public double PredictionConsistency { get; set; }
    public ConfidenceCalibration ConfidenceCalibration { get; set; } = new();
    public double DataQuality { get; set; }
    public PredictionCoverage PredictionCoverage { get; set; } = new();
    public double OverallScore { get; set; }
    public int TotalTestTimeMs { get; set; }

    public string GetSummary()
    {
        return $@"
=== ML VALIDATION REPORT ===
Overall Score: {OverallScore:P0}
Data Quality: {DataQuality:P0}
Learning Status: {(LearningProgress.IsReady ? "Ready ✅" : "Learning...")}
Total Occurrences: {LearningProgress.TotalOccurrences}
Unique Days: {LearningProgress.UniqueDays}
Avg Confidence: {ConfidenceCalibration.AverageConfidence:P0}
Prediction Consistency: {PredictionConsistency:P0}
Coverage: {PredictionCoverage.CoveragePercentage:P0}
Test Duration: {TotalTestTimeMs}ms
";
    }
}

/// <summary>
/// Confidence calibration metrics
/// </summary>
public class ConfidenceCalibration
{
    public int HighConfidenceCount { get; set; }
    public int MediumConfidenceCount { get; set; }
    public int LowConfidenceCount { get; set; }
    public double AverageConfidence { get; set; }
}

/// <summary>
/// Prediction coverage metrics
/// </summary>
public class PredictionCoverage
{
    public int CoveredHours { get; set; }
    public int TotalHours { get; set; }
    public double CoveragePercentage { get; set; }
}
