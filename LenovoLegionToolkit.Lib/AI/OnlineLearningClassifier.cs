using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Online Learning Classifier - Multi-Armed Bandit approach for workload classification
/// v7.0.0: Implements Thompson Sampling for exploration-exploitation balance
/// Learns optimal workload classification policies from user feedback (implicit via overrides)
/// </summary>
public class OnlineLearningClassifier
{
    private readonly Dictionary<string, BanditArm> _arms = new();
    private readonly object _lock = new();
    private readonly Random _random = new();

    // Thompson Sampling parameters (Beta distribution)
    private const double INITIAL_ALPHA = 1.0; // Prior successes
    private const double INITIAL_BETA = 1.0;  // Prior failures

    // Decay parameters for online learning
    private const double DECAY_RATE = 0.995; // Exponential decay per observation
    private const int MIN_OBSERVATIONS = 10; // Minimum observations before exploitation

    public OnlineLearningClassifier()
    {
        // Initialize arms for each workload type
        foreach (WorkloadType workloadType in Enum.GetValues(typeof(WorkloadType)))
        {
            if (workloadType != WorkloadType.Unknown)
            {
                _arms[workloadType.ToString()] = new BanditArm
                {
                    WorkloadType = workloadType,
                    Alpha = INITIAL_ALPHA,
                    Beta = INITIAL_BETA,
                    TotalObservations = 0,
                    LastUpdated = DateTime.Now
                };
            }
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[OnlineLearningClassifier] Initialized with {_arms.Count} bandit arms (Thompson Sampling)");
    }

    /// <summary>
    /// Select best workload classification using Thompson Sampling
    /// Balances exploration (trying new classifications) vs exploitation (using best known)
    /// </summary>
    public WorkloadType SelectWorkload(SystemContext context, Dictionary<WorkloadType, double> ruleBasedScores)
    {
        lock (_lock)
        {
            // Phase 1: Insufficient data - use rule-based classifier (exploration)
            var totalObservations = _arms.Values.Sum(a => a.TotalObservations);
            if (totalObservations < MIN_OBSERVATIONS)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[OnlineLearning] Exploration phase ({totalObservations}/{MIN_OBSERVATIONS} observations) - using rule-based classifier");

                return ruleBasedScores.OrderByDescending(kvp => kvp.Value).First().Key;
            }

            // Phase 2: Thompson Sampling - sample from Beta distribution for each arm
            var samples = new Dictionary<WorkloadType, double>();

            foreach (var arm in _arms.Values)
            {
                // Apply time decay to parameters (recent observations matter more)
                var decayedAlpha = ApplyTimeDecay(arm.Alpha, arm.LastUpdated);
                var decayedBeta = ApplyTimeDecay(arm.Beta, arm.LastUpdated);

                // Sample from Beta(alpha, beta) distribution
                var sample = SampleBetaDistribution(decayedAlpha, decayedBeta);
                samples[arm.WorkloadType] = sample;
            }

            // Select arm with highest sample (Thompson Sampling)
            var selectedWorkload = samples.OrderByDescending(kvp => kvp.Value).First().Key;

            // Bonus: Weight by rule-based confidence for safety
            var ruleConfidence = ruleBasedScores.GetValueOrDefault(selectedWorkload, 0.0);

            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"[OnlineLearning] Thompson Sampling selected: {selectedWorkload} (sample: {samples[selectedWorkload]:F3}, rule confidence: {ruleConfidence:F2})");
            }

            return selectedWorkload;
        }
    }

    /// <summary>
    /// Update bandit arm based on reward signal (implicit feedback from user behavior)
    /// Reward = 1.0 if user didn't override, 0.0 if user overrode (indicates wrong classification)
    /// </summary>
    public void UpdateReward(WorkloadType predictedWorkload, bool wasCorrect, SystemContext context)
    {
        lock (_lock)
        {
            var armKey = predictedWorkload.ToString();
            if (!_arms.ContainsKey(armKey))
                return;

            var arm = _arms[armKey];

            // Update Beta distribution parameters
            if (wasCorrect)
            {
                arm.Alpha += 1.0; // Success - increase alpha
            }
            else
            {
                arm.Beta += 1.0;  // Failure - increase beta
            }

            arm.TotalObservations++;
            arm.LastUpdated = DateTime.Now;

            // Calculate empirical success rate for diagnostics
            var successRate = arm.Alpha / (arm.Alpha + arm.Beta);

            if (Log.Instance.IsTraceEnabled)
            {
                var symbol = wasCorrect ? "✓" : "✗";
                Log.Instance.Trace($"[OnlineLearning] Updated {predictedWorkload}: {symbol} (α={arm.Alpha:F1}, β={arm.Beta:F1}, success_rate={successRate:P0}, n={arm.TotalObservations})");
            }
        }
    }

    /// <summary>
    /// Infer reward from user behavior (implicit feedback)
    /// If user overrides the workload classification within cooling period, it was wrong
    /// </summary>
    public bool InferRewardFromBehavior(
        WorkloadType predictedWorkload,
        UserPreferenceTracker? preferenceTracker,
        SystemContext context)
    {
        if (preferenceTracker == null)
            return true; // No override data - assume correct

        // Check if user overrode any workload-sensitive controls
        var workloadSensitiveControls = new[]
        {
            "POWER_MODE",
            "CPU_PL1",
            "CPU_PL2",
            "GPU_TGP",
            "FAN_PROFILE",
            "DISPLAY_BRIGHTNESS",
            "DISPLAY_REFRESH_RATE"
        };

        var overrideFrequency = 0.0;
        foreach (var control in workloadSensitiveControls)
        {
            overrideFrequency += preferenceTracker.GetOverrideFrequency(control);
        }

        // High override frequency (>0.5 overrides/hour) indicates wrong classification
        var wasCorrect = overrideFrequency < 0.5;

        if (!wasCorrect && Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"[OnlineLearning] Detected user override pattern - predicted {predictedWorkload} may be incorrect (override freq: {overrideFrequency:F2}/hr)");
        }

        return wasCorrect;
    }

    /// <summary>
    /// Sample from Beta(alpha, beta) distribution using Gamma distribution
    /// Beta(α, β) = Gamma(α) / (Gamma(α) + Gamma(β))
    /// </summary>
    private double SampleBetaDistribution(double alpha, double beta)
    {
        var x = SampleGammaDistribution(alpha);
        var y = SampleGammaDistribution(beta);

        if (x + y == 0)
            return 0.5; // Uniform prior

        return x / (x + y);
    }

    /// <summary>
    /// Sample from Gamma(shape, scale=1) distribution using Marsaglia-Tsang method
    /// </summary>
    private double SampleGammaDistribution(double shape)
    {
        if (shape < 1.0)
        {
            // For shape < 1, use: Gamma(shape) = Gamma(shape + 1) * U^(1/shape)
            return SampleGammaDistribution(shape + 1.0) * Math.Pow(_random.NextDouble(), 1.0 / shape);
        }

        // Marsaglia-Tsang method for shape >= 1
        var d = shape - 1.0 / 3.0;
        var c = 1.0 / Math.Sqrt(9.0 * d);

        while (true)
        {
            double x, v;
            do
            {
                x = SampleNormalDistribution();
                v = 1.0 + c * x;
            } while (v <= 0.0);

            v = v * v * v;
            var u = _random.NextDouble();

            // Fast accept
            if (u < 1.0 - 0.0331 * x * x * x * x)
                return d * v;

            // Slow accept
            if (Math.Log(u) < 0.5 * x * x + d * (1.0 - v + Math.Log(v)))
                return d * v;
        }
    }

    /// <summary>
    /// Sample from standard normal distribution using Box-Muller transform
    /// </summary>
    private double SampleNormalDistribution()
    {
        var u1 = _random.NextDouble();
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>
    /// Apply exponential time decay to Beta parameters
    /// Recent observations matter more than old ones
    /// </summary>
    private double ApplyTimeDecay(double parameter, DateTime lastUpdated)
    {
        var daysSinceUpdate = (DateTime.Now - lastUpdated).TotalDays;
        var decayFactor = Math.Pow(DECAY_RATE, daysSinceUpdate);

        // Decay towards prior (1.0)
        return 1.0 + (parameter - 1.0) * decayFactor;
    }

    /// <summary>
    /// Get current success rates for all workload types (diagnostics)
    /// </summary>
    public Dictionary<WorkloadType, double> GetSuccessRates()
    {
        lock (_lock)
        {
            var rates = new Dictionary<WorkloadType, double>();

            foreach (var arm in _arms.Values)
            {
                var successRate = arm.Alpha / (arm.Alpha + arm.Beta);
                rates[arm.WorkloadType] = successRate;
            }

            return rates;
        }
    }

    /// <summary>
    /// Get statistics for diagnostics
    /// </summary>
    public string GetStatistics()
    {
        lock (_lock)
        {
            var totalObservations = _arms.Values.Sum(a => a.TotalObservations);

            if (totalObservations == 0)
                return "No observations recorded (exploration phase)";

            var topArms = _arms.Values
                .OrderByDescending(a => a.Alpha / (a.Alpha + a.Beta))
                .Take(5)
                .ToList();

            var stats = $"""
                Online Learning Classifier Statistics (Thompson Sampling):
                - Total Observations: {totalObservations}
                - Learning Phase: {(totalObservations < MIN_OBSERVATIONS ? "Exploration" : "Exploitation")}
                - Decay Rate: {DECAY_RATE:P1} per observation

                Top Performing Workload Classifications:
                {string.Join("\n", topArms.Select((a, i) =>
                {
                    var successRate = a.Alpha / (a.Alpha + a.Beta);
                    return $"  {i + 1}. {a.WorkloadType}: {successRate:P0} success rate (n={a.TotalObservations})";
                }))}
                """;

            return stats;
        }
    }

    /// <summary>
    /// Export bandit arm data for persistence
    /// </summary>
    public BanditArmData ExportData()
    {
        lock (_lock)
        {
            return new BanditArmData
            {
                Arms = new Dictionary<string, BanditArm>(_arms)
            };
        }
    }

    /// <summary>
    /// Import bandit arm data from persistence
    /// </summary>
    public void ImportData(BanditArmData data)
    {
        lock (_lock)
        {
            foreach (var kvp in data.Arms)
            {
                if (_arms.ContainsKey(kvp.Key))
                {
                    _arms[kvp.Key] = kvp.Value;
                }
            }

            var totalObservations = _arms.Values.Sum(a => a.TotalObservations);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[OnlineLearningClassifier] Loaded {_arms.Count} bandit arms with {totalObservations} total observations");
        }
    }
}

/// <summary>
/// Bandit arm (workload classification option)
/// Uses Beta distribution: Beta(alpha, beta)
/// </summary>
public class BanditArm
{
    public WorkloadType WorkloadType { get; set; }

    /// <summary>
    /// Alpha parameter (successes + prior)
    /// </summary>
    public double Alpha { get; set; }

    /// <summary>
    /// Beta parameter (failures + prior)
    /// </summary>
    public double Beta { get; set; }

    /// <summary>
    /// Total observations for this arm
    /// </summary>
    public int TotalObservations { get; set; }

    /// <summary>
    /// Last time this arm was updated (for time decay)
    /// </summary>
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Bandit arm data for serialization
/// </summary>
public class BanditArmData
{
    public Dictionary<string, BanditArm> Arms { get; set; } = new();
}
