using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Thermal Uncertainty Quantifier (Step 2.2 - Elite Optimization)
///
/// Provides Bayesian prediction intervals for thermal forecasts to quantify prediction confidence.
///
/// IMPACT:
/// - Safer emergency response with confidence-based safety margins
/// - Reduced false positives (unnecessary power reductions)
/// - Better user experience (fewer aggressive interventions)
///
/// TECHNICAL APPROACH:
/// - Tracks prediction error history using sliding window
/// - Calculates prediction variance using exponential weighted moving average (EWMA)
/// - Computes confidence intervals using Student's t-distribution
/// - Adjusts safety margins based on prediction uncertainty
///
/// THEORY:
/// - High variance = low confidence = larger safety margins (conservative)
/// - Low variance = high confidence = tighter safety margins (aggressive)
/// - Typical confidence levels: 68% (1σ), 95% (2σ), 99.7% (3σ)
/// </summary>
public class ThermalUncertaintyQuantifier
{
    // Historical prediction errors for variance estimation
    private readonly Queue<PredictionError> _cpuErrors = new();
    private readonly Queue<PredictionError> _gpuErrors = new();
    private readonly Queue<PredictionError> _vrmErrors = new();

    private const int MAX_ERROR_HISTORY = 100;  // Track last 100 predictions
    private const double EWMA_ALPHA = 0.2;       // EWMA smoothing factor (slower = more stable)

    // Running variance estimates (EWMA-smoothed)
    private double _cpuVariance = 4.0;   // Initial estimate: 4°C² (2°C std dev)
    private double _gpuVariance = 4.0;   // Initial estimate: 4°C² (2°C std dev)
    private double _vrmVariance = 9.0;   // Initial estimate: 9°C² (3°C std dev, VRM more volatile)

    // Confidence level for prediction intervals (95% = 2σ)
    private const double CONFIDENCE_LEVEL = 0.95;

    // Minimum samples needed for reliable uncertainty estimates
    private const int MIN_SAMPLES_FOR_UNCERTAINTY = 10;

    /// <summary>
    /// Add a prediction error sample to update variance estimates
    /// Call this after each thermal prediction with actual observed temperature
    /// </summary>
    public void AddPredictionError(double predictedCpu, double actualCpu,
                                    double predictedGpu, double actualGpu,
                                    double predictedVrm, double actualVrm,
                                    DateTime predictionTime, DateTime actualTime)
    {
        var cpuError = actualCpu - predictedCpu;
        var gpuError = actualGpu - predictedGpu;
        var vrmError = actualVrm - predictedVrm;

        // Add to history
        _cpuErrors.Enqueue(new PredictionError
        {
            PredictionTime = predictionTime,
            ActualTime = actualTime,
            Error = cpuError,
            SquaredError = cpuError * cpuError
        });

        _gpuErrors.Enqueue(new PredictionError
        {
            PredictionTime = predictionTime,
            ActualTime = actualTime,
            Error = gpuError,
            SquaredError = gpuError * gpuError
        });

        _vrmErrors.Enqueue(new PredictionError
        {
            PredictionTime = predictionTime,
            ActualTime = actualTime,
            Error = vrmError,
            SquaredError = vrmError * vrmError
        });

        // Limit history size
        while (_cpuErrors.Count > MAX_ERROR_HISTORY)
            _cpuErrors.Dequeue();
        while (_gpuErrors.Count > MAX_ERROR_HISTORY)
            _gpuErrors.Dequeue();
        while (_vrmErrors.Count > MAX_ERROR_HISTORY)
            _vrmErrors.Dequeue();

        // Update variance estimates using EWMA
        if (_cpuErrors.Count >= MIN_SAMPLES_FOR_UNCERTAINTY)
        {
            UpdateVarianceEstimates();
        }
    }

    /// <summary>
    /// Get prediction interval for CPU temperature
    /// Returns [lower_bound, predicted, upper_bound] at specified confidence level
    /// </summary>
    public PredictionInterval GetCpuPredictionInterval(double predictedTemp)
    {
        var margin = CalculateMarginOfError(_cpuVariance, _cpuErrors.Count);
        return new PredictionInterval
        {
            Predicted = predictedTemp,
            LowerBound = predictedTemp - margin,
            UpperBound = predictedTemp + margin,
            Confidence = CONFIDENCE_LEVEL,
            StandardDeviation = Math.Sqrt(_cpuVariance)
        };
    }

    /// <summary>
    /// Get prediction interval for GPU temperature
    /// </summary>
    public PredictionInterval GetGpuPredictionInterval(double predictedTemp)
    {
        var margin = CalculateMarginOfError(_gpuVariance, _gpuErrors.Count);
        return new PredictionInterval
        {
            Predicted = predictedTemp,
            LowerBound = predictedTemp - margin,
            UpperBound = predictedTemp + margin,
            Confidence = CONFIDENCE_LEVEL,
            StandardDeviation = Math.Sqrt(_gpuVariance)
        };
    }

    /// <summary>
    /// Get prediction interval for VRM temperature
    /// </summary>
    public PredictionInterval GetVrmPredictionInterval(double predictedTemp)
    {
        var margin = CalculateMarginOfError(_vrmVariance, _vrmErrors.Count);
        return new PredictionInterval
        {
            Predicted = predictedTemp,
            LowerBound = predictedTemp - margin,
            UpperBound = predictedTemp + margin,
            Confidence = CONFIDENCE_LEVEL,
            StandardDeviation = Math.Sqrt(_vrmVariance)
        };
    }

    /// <summary>
    /// Get overall prediction confidence (0.0 to 1.0)
    /// Lower variance = higher confidence
    /// </summary>
    public double GetPredictionConfidence()
    {
        if (_cpuErrors.Count < MIN_SAMPLES_FOR_UNCERTAINTY)
            return 0.5;  // Low confidence with insufficient samples

        // Average variance across all components
        var avgVariance = (_cpuVariance + _gpuVariance + _vrmVariance) / 3.0;

        // Convert variance to confidence score
        // Low variance (1°C²) → high confidence (0.95)
        // High variance (25°C²) → low confidence (0.5)
        var confidence = 1.0 - Math.Min(avgVariance / 25.0, 0.5);

        return Math.Max(0.5, Math.Min(1.0, confidence));
    }

    /// <summary>
    /// Calculate safety margin for emergency thermal response
    /// Higher uncertainty = larger safety margin (more conservative)
    /// </summary>
    public ThermalSafetyMargins GetSafetyMargins()
    {
        var cpuStdDev = Math.Sqrt(_cpuVariance);
        var gpuStdDev = Math.Sqrt(_gpuVariance);
        var vrmStdDev = Math.Sqrt(_vrmVariance);

        return new ThermalSafetyMargins
        {
            // Use 2σ (95% confidence) as safety margin
            CpuMargin = 2.0 * cpuStdDev,
            GpuMargin = 2.0 * gpuStdDev,
            VrmMargin = 2.0 * vrmStdDev,

            // Confidence-adjusted emergency thresholds
            // If uncertain, trigger emergency earlier (more conservative)
            CpuEmergencyThreshold = 100.0 - (2.0 * cpuStdDev),  // e.g., 96°C instead of 100°C
            GpuEmergencyThreshold = 87.0 - (2.0 * gpuStdDev),   // e.g., 83°C instead of 87°C
            VrmEmergencyThreshold = 90.0 - (2.0 * vrmStdDev)    // e.g., 84°C instead of 90°C
        };
    }

    /// <summary>
    /// Get uncertainty quantification statistics for monitoring
    /// </summary>
    public UncertaintyStatistics GetStatistics()
    {
        return new UncertaintyStatistics
        {
            CpuSampleCount = _cpuErrors.Count,
            GpuSampleCount = _gpuErrors.Count,
            VrmSampleCount = _vrmErrors.Count,
            CpuStandardDeviation = Math.Sqrt(_cpuVariance),
            GpuStandardDeviation = Math.Sqrt(_gpuVariance),
            VrmStandardDeviation = Math.Sqrt(_vrmVariance),
            OverallConfidence = GetPredictionConfidence(),
            HasSufficientSamples = _cpuErrors.Count >= MIN_SAMPLES_FOR_UNCERTAINTY
        };
    }

    /// <summary>
    /// Update variance estimates using EWMA on squared errors
    /// EWMA provides smooth, responsive variance tracking
    /// </summary>
    private void UpdateVarianceEstimates()
    {
        try
        {
            // Calculate recent mean squared error (variance estimate)
            var recentCpuErrors = _cpuErrors.TakeLast(20).ToList();
            var recentGpuErrors = _gpuErrors.TakeLast(20).ToList();
            var recentVrmErrors = _vrmErrors.TakeLast(20).ToList();

            if (recentCpuErrors.Count >= 5)
            {
                var cpuMse = recentCpuErrors.Average(e => e.SquaredError);
                _cpuVariance = EWMA_ALPHA * cpuMse + (1 - EWMA_ALPHA) * _cpuVariance;
            }

            if (recentGpuErrors.Count >= 5)
            {
                var gpuMse = recentGpuErrors.Average(e => e.SquaredError);
                _gpuVariance = EWMA_ALPHA * gpuMse + (1 - EWMA_ALPHA) * _gpuVariance;
            }

            if (recentVrmErrors.Count >= 5)
            {
                var vrmMse = recentVrmErrors.Average(e => e.SquaredError);
                _vrmVariance = EWMA_ALPHA * vrmMse + (1 - EWMA_ALPHA) * _vrmVariance;
            }

            // Clamp variance to reasonable bounds (prevent outlier contamination)
            _cpuVariance = Math.Max(0.25, Math.Min(_cpuVariance, 25.0));  // 0.5°C to 5°C std dev
            _gpuVariance = Math.Max(0.25, Math.Min(_gpuVariance, 25.0));
            _vrmVariance = Math.Max(1.0, Math.Min(_vrmVariance, 36.0));   // 1°C to 6°C std dev

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[ThermalUncertainty] Variance updated: CPU={Math.Sqrt(_cpuVariance):F2}°C, GPU={Math.Sqrt(_gpuVariance):F2}°C, VRM={Math.Sqrt(_vrmVariance):F2}°C");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[ThermalUncertainty] Failed to update variance", ex);
        }
    }

    /// <summary>
    /// Calculate margin of error for prediction interval
    /// Uses Student's t-distribution for small samples, normal distribution for large samples
    /// </summary>
    private double CalculateMarginOfError(double variance, int sampleCount)
    {
        if (sampleCount < MIN_SAMPLES_FOR_UNCERTAINTY)
        {
            // Insufficient samples - use conservative margin (3σ)
            return 3.0 * Math.Sqrt(variance);
        }

        var stdDev = Math.Sqrt(variance);

        // For 95% confidence (CONFIDENCE_LEVEL = 0.95)
        // Use t-distribution critical value for small samples
        double tCritical;
        if (sampleCount < 30)
        {
            // t-distribution approximation for small samples
            // For 95% confidence, t ≈ 2.0 to 2.5 depending on df
            tCritical = 2.0 + (0.5 * (30.0 - sampleCount) / 30.0);
        }
        else
        {
            // Normal distribution z-score for 95% confidence
            tCritical = 1.96;
        }

        return tCritical * stdDev;
    }
}

/// <summary>
/// Prediction error data point
/// </summary>
public struct PredictionError
{
    public DateTime PredictionTime { get; set; }
    public DateTime ActualTime { get; set; }
    public double Error { get; set; }         // Actual - Predicted
    public double SquaredError { get; set; }  // Error²
}

/// <summary>
/// Prediction interval with confidence bounds
/// </summary>
public struct PredictionInterval
{
    public double Predicted { get; set; }
    public double LowerBound { get; set; }
    public double UpperBound { get; set; }
    public double Confidence { get; set; }    // e.g., 0.95 for 95% confidence
    public double StandardDeviation { get; set; }
}

/// <summary>
/// Safety margins for emergency thermal response
/// Adjusted based on prediction uncertainty
/// </summary>
public struct ThermalSafetyMargins
{
    public double CpuMargin { get; set; }
    public double GpuMargin { get; set; }
    public double VrmMargin { get; set; }
    public double CpuEmergencyThreshold { get; set; }
    public double GpuEmergencyThreshold { get; set; }
    public double VrmEmergencyThreshold { get; set; }
}

/// <summary>
/// Uncertainty quantification statistics
/// </summary>
public struct UncertaintyStatistics
{
    public int CpuSampleCount { get; set; }
    public int GpuSampleCount { get; set; }
    public int VrmSampleCount { get; set; }
    public double CpuStandardDeviation { get; set; }
    public double GpuStandardDeviation { get; set; }
    public double VrmStandardDeviation { get; set; }
    public double OverallConfidence { get; set; }
    public bool HasSufficientSamples { get; set; }
}
