using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Power Agent - Intelligent power envelope management
/// Integrates existing PowerUsagePredictor with battery life optimization
/// Coordinates PL1/PL2/PL4 with thermal and battery constraints
/// </summary>
public class PowerAgent : IOptimizationAgent
{
    private readonly PowerUsagePredictor _powerPredictor;
    private readonly BatteryLifeEstimator _batteryEstimator;
    private readonly PowerModeFeature _powerModeFeature;

    public string AgentName => "PowerAgent";
    public AgentPriority Priority => AgentPriority.High;

    public PowerAgent(
        PowerUsagePredictor powerPredictor,
        BatteryLifeEstimator batteryEstimator,
        PowerModeFeature powerModeFeature)
    {
        _powerPredictor = powerPredictor ?? throw new ArgumentNullException(nameof(powerPredictor));
        _batteryEstimator = batteryEstimator ?? throw new ArgumentNullException(nameof(batteryEstimator));
        _powerModeFeature = powerModeFeature ?? throw new ArgumentNullException(nameof(powerModeFeature));
    }

    public async Task<AgentProposal> ProposeActionsAsync(SystemContext context)
    {
        var proposal = new AgentProposal
        {
            Agent = AgentName,
            Priority = Priority
        };

        // Record data point for ML model
        RecordPowerDataPoint(context);

        // Battery-aware power management
        if (context.BatteryState.IsOnBattery)
        {
            await HandleBatteryPowerAsync(proposal, context).ConfigureAwait(false);
        }
        // AC Power - maximize performance within constraints
        else
        {
            await HandleACPowerAsync(proposal, context).ConfigureAwait(false);
        }

        // Check for power mode optimization (using existing ML predictor)
        await OptimizePowerModeAsync(proposal, context).ConfigureAwait(false);

        return proposal;
    }

    public Task OnActionsExecutedAsync(ExecutionResult result)
    {
        // Learn from power management outcomes
        if (result.Success)
        {
            var powerBefore = result.ContextBefore.PowerState.TotalSystemPower;
            var powerAfter = result.ContextAfter.PowerState.TotalSystemPower;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power action result: {powerBefore}W -> {powerAfter}W");

            // TODO: Update ML model with outcome
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle power management on battery
    /// </summary>
    private async Task HandleBatteryPowerAsync(AgentProposal proposal, SystemContext context)
    {
        var timeRemaining = _batteryEstimator.EstimateTimeRemaining(context);
        var batteryPercent = context.BatteryState.ChargePercent;

        // CRITICAL: Less than 20 minutes or 15% battery
        if (timeRemaining < TimeSpan.FromMinutes(20) || batteryPercent < 15)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Critical,
                Target = "POWER_MODE",
                Value = PowerModeState.Quiet,
                Reason = $"Battery critical: {timeRemaining.TotalMinutes:F0}m remaining ({batteryPercent}%)"
            });

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Critical,
                Target = "CPU_PL1",
                Value = 15, // Minimum sustainable
                Reason = "Battery conservation - minimum power mode"
            });

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Critical,
                Target = "CPU_PL2",
                Value = 55,
                Reason = "Battery conservation - reduce turbo"
            });

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Critical,
                Target = "GPU_TGP",
                Value = 60,
                Reason = "Battery conservation - minimize GPU power"
            });
        }
        // PROACTIVE: Battery below 50% and not gaming
        else if (batteryPercent < 50 && context.UserIntent != UserIntent.Gaming)
        {
            // Predict if user will need more battery later
            var futureNeed = await _batteryEstimator.PredictFutureBatteryNeedAsync(context).ConfigureAwait(false);

            if (futureNeed.Confidence > 0.7 && futureNeed.PredictedNeed > 0.5)
            {
                proposal.Actions.Add(new ResourceAction
                {
                    Type = ActionType.Proactive,
                    Target = "CPU_PL2",
                    Value = 75,
                    Reason = $"Preserving battery for predicted demand at {futureNeed.PredictedTime:HH:mm}"
                });

                proposal.Actions.Add(new ResourceAction
                {
                    Type = ActionType.Proactive,
                    Target = "GPU_TGP",
                    Value = 80,
                    Reason = "Battery preservation mode"
                });
            }
        }
        // BALANCED: Normal battery management
        else if (context.UserIntent != UserIntent.Gaming && context.UserIntent != UserIntent.MaxPerformance)
        {
            // Suggest balanced power limits for battery life
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "CPU_PL2",
                Value = 90,
                Reason = "Battery-optimized performance"
            });
        }
    }

    /// <summary>
    /// Handle power management on AC power
    /// </summary>
    private Task HandleACPowerAsync(AgentProposal proposal, SystemContext context)
    {
        var thermalHeadroom = 95 - context.ThermalState.CpuTemp;

        // High thermal headroom - can boost performance
        if (thermalHeadroom > 15 &&
            (context.UserIntent == UserIntent.Gaming || context.UserIntent == UserIntent.MaxPerformance))
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "CPU_PL2",
                Value = 140, // Max Gen 9 turbo
                Reason = $"AC power + thermal headroom: {thermalHeadroom}°C"
            });

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "GPU_TGP",
                Value = 140, // Max GPU TGP
                Reason = "Maximum performance mode"
            });
        }
        // Moderate thermal headroom
        else if (thermalHeadroom > 8)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "CPU_PL2",
                Value = 115,
                Reason = "Balanced performance on AC"
            });
        }
        // Limited thermal headroom - conservative power
        else if (thermalHeadroom < 5)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "CPU_PL2",
                Value = 90,
                Reason = $"Thermal constraint: only {thermalHeadroom}°C headroom"
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Use existing PowerUsagePredictor ML model for power mode optimization
    /// </summary>
    private async Task OptimizePowerModeAsync(AgentProposal proposal, SystemContext context)
    {
        if (!FeatureFlags.UseMLAIController)
            return;

        var currentMode = await _powerModeFeature.GetStateAsync().ConfigureAwait(false);

        // Get ML prediction from existing predictor
        var suggestion = _powerPredictor.GetPowerModeSuggestion(
            currentMode,
            context.CurrentWorkload.CpuUtilizationPercent,
            context.ThermalState.CpuTemp,
            context.BatteryState.IsOnBattery,
            context.Timestamp.TimeOfDay
        );

        if (suggestion.ShouldSwitch && suggestion.RecommendedMode != currentMode)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "POWER_MODE",
                Value = suggestion.RecommendedMode,
                Reason = $"ML prediction: {suggestion.Reason}"
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ML suggests power mode switch: {currentMode} -> {suggestion.RecommendedMode}");
        }
    }

    /// <summary>
    /// Record power usage data for ML model training
    /// </summary>
    private void RecordPowerDataPoint(SystemContext context)
    {
        if (!FeatureFlags.UseMLAIController)
            return;

        var dataPoint = new PowerUsageDataPoint
        {
            PowerMode = context.PowerState.CurrentPowerMode,
            CpuUsagePercent = context.CurrentWorkload.CpuUtilizationPercent,
            CpuTemperature = context.ThermalState.CpuTemp,
            IsOnBattery = context.BatteryState.IsOnBattery,
            TimeOfDay = context.Timestamp.TimeOfDay,
            Timestamp = context.Timestamp
        };

        _powerPredictor.RecordDataPoint(dataPoint);
    }
}

/// <summary>
/// Battery life estimator with predictive capabilities
/// </summary>
public class BatteryLifeEstimator
{
    private readonly LinkedList<BatteryDataPoint> _history = new();
    private const int MaxHistorySize = 500; // ~8 minutes at 1Hz

    /// <summary>
    /// Estimate time remaining based on current discharge rate and workload
    /// </summary>
    public TimeSpan EstimateTimeRemaining(SystemContext context)
    {
        if (!context.BatteryState.IsOnBattery)
            return TimeSpan.MaxValue;

        var chargePercent = context.BatteryState.ChargePercent;
        var dischargeRateMw = Math.Abs(context.BatteryState.ChargeRateMw);

        if (dischargeRateMw <= 0)
            return context.BatteryState.EstimatedTimeRemaining;

        // Calculate based on current rate
        var capacityMwh = context.BatteryState.FullChargeCapacityMwh;
        var remainingMwh = (capacityMwh * chargePercent) / 100.0;
        var hoursRemaining = remainingMwh / dischargeRateMw;

        // Adjust for workload (gaming drains faster)
        if (context.CurrentWorkload.Type == WorkloadType.Gaming)
            hoursRemaining *= 0.85; // Gaming typically 15% faster drain

        return TimeSpan.FromHours(Math.Max(0, hoursRemaining));
    }

    /// <summary>
    /// Predict if user will need battery later (ML-based on usage patterns)
    /// </summary>
    public Task<BatteryNeedPrediction> PredictFutureBatteryNeedAsync(SystemContext context)
    {
        // Record current battery state
        RecordBatteryDataPoint(context);

        // Simple heuristic-based prediction (can be enhanced with ML)
        var currentHour = context.Timestamp.Hour;

        // Common patterns:
        // - Morning commute (7-9 AM): High need
        // - Lunch break (12-1 PM): Medium need
        // - Evening activities (6-10 PM): High need
        var needScore = currentHour switch
        {
            >= 7 and < 9 => 0.8,   // Morning
            >= 12 and < 13 => 0.6, // Lunch
            >= 18 and < 22 => 0.9, // Evening
            _ => 0.3               // Low probability
        };

        return Task.FromResult(new BatteryNeedPrediction
        {
            PredictedNeed = needScore,
            Confidence = 0.7,
            PredictedTime = context.Timestamp.AddHours(1),
            Reason = "Usage pattern analysis"
        });
    }

    private void RecordBatteryDataPoint(SystemContext context)
    {
        var dataPoint = new BatteryDataPoint
        {
            Timestamp = context.Timestamp,
            ChargePercent = context.BatteryState.ChargePercent,
            DischargeRateMw = context.BatteryState.ChargeRateMw,
            WorkloadType = context.CurrentWorkload.Type
        };

        _history.AddLast(dataPoint);
        while (_history.Count > MaxHistorySize)
            _history.RemoveFirst();
    }
}

public class BatteryDataPoint
{
    public DateTime Timestamp { get; set; }
    public int ChargePercent { get; set; }
    public int DischargeRateMw { get; set; }
    public WorkloadType WorkloadType { get; set; }
}

public class BatteryNeedPrediction
{
    public double PredictedNeed { get; set; }  // 0-1 scale
    public double Confidence { get; set; }     // 0-1 scale
    public DateTime PredictedTime { get; set; }
    public string Reason { get; set; } = string.Empty;
}
