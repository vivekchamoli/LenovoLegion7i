using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Thermal Agent - Multi-horizon predictive thermal management
/// Prevents thermal throttling through proactive cooling adjustments
/// Horizons: 15s (emergency), 60s (proactive), 300s (strategic)
/// </summary>
public class ThermalAgent : IOptimizationAgent
{
    private readonly ThermalOptimizer _thermalOptimizer;
    private readonly SystemContextStore _contextStore;

    // Multi-horizon prediction intervals
    private const int SHORT_HORIZON_SEC = 15;   // Emergency response
    private const int MEDIUM_HORIZON_SEC = 60;  // Proactive cooling
    private const int LONG_HORIZON_SEC = 300;   // Strategic adaptation

    // Gen 9 thermal thresholds (Legion Slim 7i)
    private const int CPU_THROTTLE_TEMP = 95;   // CPU starts throttling
    private const int GPU_THROTTLE_TEMP = 87;   // GPU starts throttling
    private const int SAFE_CPU_TEMP = 85;       // Target safe temperature
    private const int SAFE_GPU_TEMP = 75;       // Target safe temperature

    public string AgentName => "ThermalAgent";
    public AgentPriority Priority => AgentPriority.Critical;

    public ThermalAgent(ThermalOptimizer thermalOptimizer, SystemContextStore contextStore)
    {
        _thermalOptimizer = thermalOptimizer ?? throw new ArgumentNullException(nameof(thermalOptimizer));
        _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
    }

    public async Task<AgentProposal> ProposeActionsAsync(SystemContext context)
    {
        var proposal = new AgentProposal
        {
            Agent = AgentName,
            Priority = Priority
        };

        // Multi-horizon thermal predictions
        var predictions = await PredictMultiHorizonTemperaturesAsync(context).ConfigureAwait(false);

        // Log predictions for telemetry
        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Thermal predictions - Short: CPU={predictions.ShortHorizonCpuTemp:F1}°C GPU={predictions.ShortHorizonGpuTemp:F1}°C, Medium: CPU={predictions.MediumHorizonCpuTemp:F1}°C GPU={predictions.MediumHorizonGpuTemp:F1}°C");
        }

        // EMERGENCY ACTIONS (15-second horizon)
        if (predictions.ShortHorizonCpuTemp >= CPU_THROTTLE_TEMP - 3 ||
            predictions.ShortHorizonGpuTemp >= GPU_THROTTLE_TEMP - 3)
        {
            AddEmergencyThermalActions(proposal, context, predictions);
        }
        // PROACTIVE ACTIONS (60-second horizon)
        else if (predictions.MediumHorizonCpuTemp >= CPU_THROTTLE_TEMP - 10 ||
                 predictions.MediumHorizonGpuTemp >= GPU_THROTTLE_TEMP - 10)
        {
            AddProactiveThermalActions(proposal, context, predictions);
        }
        // OPPORTUNISTIC ACTIONS (300-second horizon)
        else if (predictions.LongHorizonCpuTemp < 65 &&
                 predictions.LongHorizonGpuTemp < 60 &&
                 context.ThermalState.Trend.IsStable)
        {
            AddOpportunisticThermalActions(proposal, context);
        }

        // VRM temperature management (often overlooked but critical)
        if (context.ThermalState.VrmTemp > 85)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Emergency,
                Target = "CPU_PL1",
                Value = Math.Max(35, context.PowerState.CurrentPL1 - 15),
                Reason = $"VRM overheating: {context.ThermalState.VrmTemp}°C (reducing sustained power)"
            });
        }

        return proposal;
    }

    public Task OnActionsExecutedAsync(ExecutionResult result)
    {
        // Learn from thermal action outcomes
        if (result.Success && result.ExecutedActions.Any(a => a.Target.Contains("FAN") || a.Target.Contains("PL")))
        {
            var tempBefore = result.ContextBefore.ThermalState.CpuTemp;
            var tempAfter = result.ContextAfter.ThermalState.CpuTemp;
            var tempDelta = tempAfter - tempBefore;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Thermal action result: {tempBefore}°C -> {tempAfter}°C (Δ={tempDelta:+0;-0}°C)");

            // TODO: Store this for ML model improvement
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Multi-horizon temperature prediction using historical trends
    /// </summary>
    private Task<MultiHorizonThermalPredictions> PredictMultiHorizonTemperaturesAsync(SystemContext context)
    {
        var history = _contextStore.GetThermalHistory();

        if (history.Count < 10)
        {
            // Insufficient data - use simple linear projection
            return Task.FromResult(new MultiHorizonThermalPredictions
            {
                ShortHorizonCpuTemp = context.ThermalState.CpuTemp + (context.ThermalState.Trend.CpuTrendPerSecond * SHORT_HORIZON_SEC),
                ShortHorizonGpuTemp = context.ThermalState.GpuTemp + (context.ThermalState.Trend.GpuTrendPerSecond * SHORT_HORIZON_SEC),
                MediumHorizonCpuTemp = context.ThermalState.CpuTemp + (context.ThermalState.Trend.CpuTrendPerSecond * MEDIUM_HORIZON_SEC),
                MediumHorizonGpuTemp = context.ThermalState.GpuTemp + (context.ThermalState.Trend.GpuTrendPerSecond * MEDIUM_HORIZON_SEC),
                LongHorizonCpuTemp = context.ThermalState.CpuTemp + (context.ThermalState.Trend.CpuTrendPerSecond * LONG_HORIZON_SEC),
                LongHorizonGpuTemp = context.ThermalState.GpuTemp + (context.ThermalState.Trend.GpuTrendPerSecond * LONG_HORIZON_SEC),
                Confidence = 0.5
            });
        }

        // Use ThermalOptimizer's advanced prediction
        // Note: ThermalState from history already has all required properties
        var optimizerPredictions = _thermalOptimizer.PredictThermalState(
            history.ToList(),
            MEDIUM_HORIZON_SEC
        );

        // Enhanced predictions with pattern matching
        var recentHistory = history.TakeLast(30).ToList();
        var cpuTrend = CalculateAcceleratedTrend(recentHistory.Select(h => h.CpuTemp).ToList());
        var gpuTrend = CalculateAcceleratedTrend(recentHistory.Select(h => h.GpuTemp).ToList());

        return Task.FromResult(new MultiHorizonThermalPredictions
        {
            ShortHorizonCpuTemp = Math.Max(0, context.ThermalState.CpuTemp + (cpuTrend * SHORT_HORIZON_SEC)),
            ShortHorizonGpuTemp = Math.Max(0, context.ThermalState.GpuTemp + (gpuTrend * SHORT_HORIZON_SEC)),
            MediumHorizonCpuTemp = optimizerPredictions.PredictedCpuTemp,
            MediumHorizonGpuTemp = optimizerPredictions.PredictedGpuTemp,
            LongHorizonCpuTemp = Math.Max(0, context.ThermalState.CpuTemp + (cpuTrend * LONG_HORIZON_SEC * 0.7)), // Damping factor
            LongHorizonGpuTemp = Math.Max(0, context.ThermalState.GpuTemp + (gpuTrend * LONG_HORIZON_SEC * 0.7)),
            Confidence = optimizerPredictions.Confidence
        });
    }

    private void AddEmergencyThermalActions(
        AgentProposal proposal,
        SystemContext context,
        MultiHorizonThermalPredictions predictions)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"THERMAL EMERGENCY: Predicted throttling in {SHORT_HORIZON_SEC}s");

        // Immediate power reduction
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Emergency,
            Target = "CPU_PL2",
            Value = Math.Max(90, context.PowerState.CurrentPL2 - 25),
            Reason = $"Emergency thermal response: {predictions.ShortHorizonCpuTemp:F1}°C predicted"
        });

        // Maximum fan speed
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Emergency,
            Target = "FAN_PROFILE",
            Value = FanProfile.MaxPerformance,
            Reason = "Emergency cooling - prevent throttling"
        });

        // GPU power reduction if GPU is hot
        if (predictions.ShortHorizonGpuTemp >= GPU_THROTTLE_TEMP - 3)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Emergency,
                Target = "GPU_TGP",
                Value = Math.Max(90, context.PowerState.GpuTGP - 30),
                Reason = $"GPU thermal emergency: {predictions.ShortHorizonGpuTemp:F1}°C predicted"
            });
        }
    }

    private void AddProactiveThermalActions(
        AgentProposal proposal,
        SystemContext context,
        MultiHorizonThermalPredictions predictions)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"THERMAL PROACTIVE: Increasing cooling preemptively");

        // Gradual power adjustment
        var targetPL2 = context.PowerState.CurrentPL2 - 15;
        if (targetPL2 >= 100)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "CPU_PL2",
                Value = targetPL2,
                Reason = $"Proactive thermal management: {predictions.MediumHorizonCpuTemp:F1}°C predicted in {MEDIUM_HORIZON_SEC}s"
            });
        }

        // Increase fan aggressiveness
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "FAN_PROFILE",
            Value = FanProfile.Aggressive,
            Reason = "Preemptive cooling ramp"
        });
    }

    private void AddOpportunisticThermalActions(
        AgentProposal proposal,
        SystemContext context)
    {
        // System is cool and stable - can enable quiet mode or increase performance
        if (context.UserIntent == UserIntent.Quiet || context.UserIntent == UserIntent.BatterySaving)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "FAN_PROFILE",
                Value = FanProfile.Quiet,
                Reason = $"Thermal headroom available ({context.ThermalState.CpuTemp}°C) - enabling quiet mode"
            });
        }
        else if (context.UserIntent == UserIntent.MaxPerformance || context.UserIntent == UserIntent.Gaming)
        {
            // Thermal headroom available - can boost performance
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "CPU_PL2",
                Value = Math.Min(140, context.PowerState.CurrentPL2 + 15),
                Reason = $"Thermal headroom ({CPU_THROTTLE_TEMP - context.ThermalState.CpuTemp}°C) - boosting performance"
            });
        }
    }

    /// <summary>
    /// Calculate accelerated trend considering acceleration/deceleration
    /// More accurate than simple linear regression for rapid thermal changes
    /// </summary>
    private double CalculateAcceleratedTrend(List<byte> temperatures)
    {
        if (temperatures.Count < 5)
            return 0;

        // Calculate velocity (first derivative)
        var velocities = new List<double>();
        for (int i = 1; i < temperatures.Count; i++)
        {
            velocities.Add(temperatures[i] - temperatures[i - 1]);
        }

        // Calculate acceleration (second derivative)
        var accelerations = new List<double>();
        for (int i = 1; i < velocities.Count; i++)
        {
            accelerations.Add(velocities[i] - velocities[i - 1]);
        }

        var avgVelocity = velocities.TakeLast(5).Average();
        var avgAcceleration = accelerations.Any() ? accelerations.TakeLast(3).Average() : 0;

        // Project with acceleration: v + a*t
        return avgVelocity + (avgAcceleration * 0.5);
    }
}

/// <summary>
/// Multi-horizon thermal predictions
/// </summary>
public class MultiHorizonThermalPredictions
{
    public double ShortHorizonCpuTemp { get; set; }
    public double ShortHorizonGpuTemp { get; set; }
    public double MediumHorizonCpuTemp { get; set; }
    public double MediumHorizonGpuTemp { get; set; }
    public double LongHorizonCpuTemp { get; set; }
    public double LongHorizonGpuTemp { get; set; }
    public double Confidence { get; set; }
}
