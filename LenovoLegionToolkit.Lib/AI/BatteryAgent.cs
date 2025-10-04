using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Battery Agent - Intelligent battery health and power management
/// Manages charge rates, longevity optimization, and coordinated conservation
/// Priority: Critical (battery health is paramount for laptop usability)
/// </summary>
public class BatteryAgent : IOptimizationAgent
{
    private readonly BatteryFeature _batteryFeature;
    private readonly BatteryLifeEstimator _batteryEstimator;

    // Battery health optimization parameters
    private const int CRITICAL_BATTERY_PERCENT = 15;
    private const int LOW_BATTERY_PERCENT = 30;
    private const int MEDIUM_BATTERY_PERCENT = 50;

    public string AgentName => "BatteryAgent";
    public AgentPriority Priority => AgentPriority.Critical;

    public BatteryAgent(
        BatteryFeature batteryFeature,
        BatteryLifeEstimator batteryEstimator)
    {
        _batteryFeature = batteryFeature ?? throw new ArgumentNullException(nameof(batteryFeature));
        _batteryEstimator = batteryEstimator ?? throw new ArgumentNullException(nameof(batteryEstimator));
    }

    public async Task<AgentProposal> ProposeActionsAsync(SystemContext context)
    {
        var proposal = new AgentProposal
        {
            Agent = AgentName,
            Priority = Priority
        };

        // Skip if battery feature not available
        if (!await _batteryFeature.IsSupportedAsync().ConfigureAwait(false))
            return proposal;

        // Analyze battery state and propose actions
        if (context.BatteryState.IsOnBattery)
        {
            await HandleBatteryDischargeAsync(proposal, context).ConfigureAwait(false);
        }

        return proposal;
    }

    public Task OnActionsExecutedAsync(ExecutionResult result)
    {
        // Learn from battery management outcomes
        if (result.Success && result.ExecutedActions.Any(a => a.Target.Contains("BATTERY")))
        {
            if (Log.Instance.IsTraceEnabled)
            {
                var batteryBefore = result.ContextBefore.BatteryState.ChargePercent;
                var batteryAfter = result.ContextAfter.BatteryState.ChargePercent;
                Log.Instance.Trace($"Battery action result: {batteryBefore}% -> {batteryAfter}%");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle battery discharge scenarios (on battery power)
    /// </summary>
    private async Task HandleBatteryDischargeAsync(AgentProposal proposal, SystemContext context)
    {
        var chargePercent = context.BatteryState.ChargePercent;
        var timeRemaining = _batteryEstimator.EstimateTimeRemaining(context);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Battery discharge: {chargePercent}%, ~{timeRemaining.TotalMinutes:F0} minutes remaining");

        // CRITICAL: Battery extremely low (<15% or <20 min)
        if (chargePercent < CRITICAL_BATTERY_PERCENT || timeRemaining < TimeSpan.FromMinutes(20))
        {
            await ProposeEmergencyConservationAsync(proposal, context).ConfigureAwait(false);
        }
        // LOW: Battery getting low (<30%)
        else if (chargePercent < LOW_BATTERY_PERCENT)
        {
            await ProposeLowBatteryConservationAsync(proposal, context).ConfigureAwait(false);
        }
        // MEDIUM: Moderate conservation (<50% and not gaming)
        else if (chargePercent < MEDIUM_BATTERY_PERCENT && context.UserIntent != UserIntent.Gaming)
        {
            await ProposeModerateBatteryConservationAsync(proposal, context).ConfigureAwait(false);
        }

        // Analyze discharge rate and warn if abnormal
        await AnalyzeDischargeRateAsync(proposal, context).ConfigureAwait(false);
    }

    /// <summary>
    /// Emergency battery conservation mode - coordinate all agents
    /// </summary>
    private Task ProposeEmergencyConservationAsync(AgentProposal proposal, SystemContext context)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"BATTERY EMERGENCY: Activating coordinated conservation mode");

        // This is a critical coordinated action - all agents should conserve
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Critical,
            Target = "COORDINATE_EMERGENCY_MODE",
            Value = true,
            Reason = $"Battery critical: {context.BatteryState.ChargePercent}% - maximum conservation"
        });

        // Enable battery conservation mode
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Critical,
            Target = "BATTERY_CONSERVATION_MODE",
            Value = true,
            Reason = "Emergency battery saving"
        });

        // Suggest hibernation if battery continues to drop
        if (context.BatteryState.ChargePercent < 10)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Critical,
                Target = "SYSTEM_HIBERNATE_WARNING",
                Value = true,
                Reason = $"Battery at {context.BatteryState.ChargePercent}% - consider hibernating"
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Low battery conservation (proactive)
    /// </summary>
    private Task ProposeLowBatteryConservationAsync(AgentProposal proposal, SystemContext context)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"BATTERY LOW: Proposing proactive conservation");

        // Enable battery conservation mode
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "BATTERY_CONSERVATION_MODE",
            Value = true,
            Reason = $"Battery low: {context.BatteryState.ChargePercent}%"
        });

        // Signal other agents to conserve (they will see this in context)
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "COORDINATE_LOW_BATTERY_MODE",
            Value = true,
            Reason = "Battery low - request conservation from all agents"
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Moderate battery conservation (opportunistic)
    /// </summary>
    private async Task ProposeModerateBatteryConservationAsync(AgentProposal proposal, SystemContext context)
    {
        // Check if user will need battery later
        var futureNeed = await _batteryEstimator.PredictFutureBatteryNeedAsync(context).ConfigureAwait(false);

        if (futureNeed.Confidence > 0.6 && futureNeed.PredictedNeed > 0.5)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "BATTERY_CONSERVATION_MODE",
                Value = true,
                Reason = $"Preserving battery for predicted demand at {futureNeed.PredictedTime:HH:mm}"
            });
        }
    }

    /// <summary>
    /// Analyze discharge rate for anomalies
    /// </summary>
    private Task AnalyzeDischargeRateAsync(AgentProposal proposal, SystemContext context)
    {
        var dischargeRateMw = Math.Abs(context.BatteryState.ChargeRateMw);

        // High discharge rate (>40W) - something is consuming excessive power
        if (dischargeRateMw > 40000)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"High battery discharge rate detected: {dischargeRateMw / 1000.0:F1}W");

            // This signals to other agents that power consumption is too high
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "COORDINATE_HIGH_POWER_CONSUMPTION",
                Value = dischargeRateMw,
                Reason = $"Excessive power draw: {dischargeRateMw / 1000.0:F1}W"
            });
        }

        return Task.CompletedTask;
    }
}
