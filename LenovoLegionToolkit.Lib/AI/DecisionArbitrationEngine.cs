using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Decision arbitration engine for resolving conflicts between agent proposals
/// Priority hierarchy: Emergency > Critical > Proactive > Reactive > Opportunistic
/// </summary>
public class DecisionArbitrationEngine
{
    /// <summary>
    /// Resolve conflicts between multiple agent proposals
    /// Returns unified execution plan with conflict documentation
    /// </summary>
    public async Task<ExecutionPlan> ResolveAsync(
        IEnumerable<AgentProposal> proposals,
        SystemContext context)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Arbitrating {proposals.Count()} agent proposals...");

        var plan = new ExecutionPlan
        {
            CreatedAt = DateTime.UtcNow
        };

        // Flatten all actions from all proposals (use named tuples)
        var allActions = proposals
            .SelectMany(p => p.Actions.Select(a => (Proposal: p, Action: a)))
            .ToList();

        // Group actions by target resource
        var actionsByTarget = allActions
            .GroupBy(x => x.Action.Target)
            .ToList();

        foreach (var group in actionsByTarget)
        {
            var target = group.Key;
            var conflictingActions = group.Select(x => (Proposal: x.Proposal, Action: x.Action)).ToList();

            if (conflictingActions.Count == 1)
            {
                // No conflict - add action directly
                plan.Actions.Add(conflictingActions[0].Action);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"No conflict for {target} - adding action from {conflictingActions[0].Proposal.Agent}");

                continue;
            }

            // CONFLICT RESOLUTION LOGIC
            var resolvedAction = await ResolveConflictAsync(conflictingActions, context).ConfigureAwait(false);

            plan.Actions.Add(resolvedAction.Action);

            // Document the conflict
            var conflict = new Conflict
            {
                Target = target,
                Winner = resolvedAction.Action,
                Losers = conflictingActions
                    .Where(a => a.Action != resolvedAction.Action)
                    .Select(a => a.Action)
                    .ToList(),
                Reason = $"Resolved by {GetResolutionStrategy(conflictingActions, context)}"
            };

            plan.Conflicts.Add(conflict);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Conflict resolved for {target}: Winner={resolvedAction.Proposal.Agent}, Losers={string.Join(", ", conflict.Losers.Select(l => GetActionAgent(l, proposals)))}");
        }

        // Add execution metrics
        plan.Metrics["total_proposals"] = proposals.Count();
        plan.Metrics["total_actions"] = plan.Actions.Count;
        plan.Metrics["conflicts_resolved"] = plan.Conflicts.Count;
        plan.Metrics["emergency_actions"] = plan.Actions.Count(a => a.Type == ActionType.Emergency);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Arbitration complete: {plan.Actions.Count} actions, {plan.Conflicts.Count} conflicts");

        return plan;
    }

    private Task<(AgentProposal Proposal, ResourceAction Action)> ResolveConflictAsync(
        List<(AgentProposal Proposal, ResourceAction Action)> conflictingActions,
        SystemContext context)
    {
        // PRIORITY 1: Emergency thermal actions always win
        var emergency = conflictingActions.FirstOrDefault(a => a.Action.Type == ActionType.Emergency);
        if (emergency.Action != null)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Emergency action from {emergency.Proposal.Agent} takes priority");

            return Task.FromResult(emergency);
        }

        // PRIORITY 2: Battery critical situations
        var batteryCritical = conflictingActions.FirstOrDefault(a =>
            a.Action.Type == ActionType.Critical &&
            a.Action.Reason.Contains("Battery", StringComparison.OrdinalIgnoreCase));

        if (batteryCritical.Action != null)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery critical action from {batteryCritical.Proposal.Agent} takes priority");

            return Task.FromResult(batteryCritical);
        }

        // PRIORITY 3: User intent override
        if (context.UserIntent == UserIntent.MaxPerformance || context.UserIntent == UserIntent.Gaming)
        {
            // Prefer performance-oriented actions
            var performanceAction = conflictingActions
                .OrderByDescending(a => GetPerformanceScore(a.Action))
                .First();

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"User intent (performance) resolved to {performanceAction.Proposal.Agent}");

            return Task.FromResult(performanceAction);
        }

        if (context.UserIntent == UserIntent.BatterySaving || context.UserIntent == UserIntent.Quiet)
        {
            // Prefer efficiency-oriented actions
            var efficiencyAction = conflictingActions
                .OrderBy(a => GetPowerConsumptionScore(a.Action))
                .First();

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"User intent (efficiency) resolved to {efficiencyAction.Proposal.Agent}");

            return Task.FromResult(efficiencyAction);
        }

        // PRIORITY 4: Action type priority (Proactive > Reactive > Opportunistic)
        var byActionType = conflictingActions
            .OrderByDescending(a => (int)a.Action.Type)
            .First();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Action type priority resolved to {byActionType.Proposal.Agent} (type: {byActionType.Action.Type})");

        return Task.FromResult(byActionType);
    }

    private string GetResolutionStrategy(
        List<(AgentProposal Proposal, ResourceAction Action)> conflictingActions,
        SystemContext context)
    {
        if (conflictingActions.Any(a => a.Action.Type == ActionType.Emergency))
            return "Emergency Override";

        if (conflictingActions.Any(a => a.Action.Type == ActionType.Critical))
            return "Critical Priority";

        if (context.UserIntent == UserIntent.MaxPerformance || context.UserIntent == UserIntent.Gaming)
            return "User Intent: Performance";

        if (context.UserIntent == UserIntent.BatterySaving || context.UserIntent == UserIntent.Quiet)
            return "User Intent: Efficiency";

        return "Action Type Priority";
    }

    private string GetActionAgent(ResourceAction action, IEnumerable<AgentProposal> proposals)
    {
        return proposals
            .FirstOrDefault(p => p.Actions.Contains(action))?.Agent ?? "Unknown";
    }

    /// <summary>
    /// Calculate performance impact score (higher = more performance)
    /// Used for performance-oriented conflict resolution
    /// </summary>
    private double GetPerformanceScore(ResourceAction action)
    {
        return action.Target.ToLowerInvariant() switch
        {
            "cpu_pl2" => ConvertToDouble(action.Value) / 140.0 * 100, // Normalize to 0-100
            "cpu_pl1" => ConvertToDouble(action.Value) / 55.0 * 100,
            "gpu_tgp" => ConvertToDouble(action.Value) / 140.0 * 100,
            "power_mode" when action.Value is PowerModeState mode => mode switch
            {
                PowerModeState.Performance => 100,
                PowerModeState.Balance => 60,
                PowerModeState.Quiet => 30,
                _ => 50
            },
            "fan_profile" when action.Value is FanProfile fan => fan switch
            {
                FanProfile.MaxPerformance => 100,
                FanProfile.Aggressive => 80,
                FanProfile.Balanced => 50,
                FanProfile.Quiet => 20,
                _ => 50
            },
            "gpu_overclock" => 90,
            _ => 50 // Default neutral score
        };
    }

    /// <summary>
    /// Calculate power consumption score (lower = more efficient)
    /// Used for battery-saving conflict resolution
    /// </summary>
    private double GetPowerConsumptionScore(ResourceAction action)
    {
        return action.Target.ToLowerInvariant() switch
        {
            "cpu_pl2" => ConvertToDouble(action.Value), // Raw wattage
            "cpu_pl1" => ConvertToDouble(action.Value),
            "gpu_tgp" => ConvertToDouble(action.Value),
            "power_mode" when action.Value is PowerModeState mode => mode switch
            {
                PowerModeState.Performance => 140,
                PowerModeState.Balance => 80,
                PowerModeState.Quiet => 40,
                _ => 80
            },
            "fan_profile" when action.Value is FanProfile fan => fan switch
            {
                FanProfile.MaxPerformance => 100,
                FanProfile.Aggressive => 70,
                FanProfile.Balanced => 40,
                FanProfile.Quiet => 20,
                _ => 40
            },
            "gpu_overclock" => 120, // High power consumption
            "gpu_power_state" when action.Value?.ToString() == "D3Cold" => 5, // Very low power
            _ => 50 // Default neutral score
        };
    }

    private double ConvertToDouble(object value)
    {
        return value switch
        {
            int i => i,
            double d => d,
            float f => f,
            byte b => b,
            _ => 0
        };
    }

    /// <summary>
    /// Validate execution plan for safety and coherence
    /// Ensures no conflicting or dangerous action combinations
    /// </summary>
    public bool ValidateExecutionPlan(ExecutionPlan plan, SystemContext context)
    {
        // Check for thermal safety
        var hasThermalRisk = context.ThermalState.CpuTemp > 90 || context.ThermalState.GpuTemp > 85;
        var hasPerformanceIncrease = plan.Actions.Any(a =>
            (a.Target == "CPU_PL2" && ConvertToDouble(a.Value) > 120) ||
            (a.Target == "GPU_TGP" && ConvertToDouble(a.Value) > 120));

        if (hasThermalRisk && hasPerformanceIncrease)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"SAFETY VIOLATION: Attempting performance increase while thermals critical");

            return false;
        }

        // Check for battery safety
        var isBatteryCritical = context.BatteryState.IsOnBattery &&
                               context.BatteryState.ChargePercent < 20;
        var hasHighPowerAction = plan.Actions.Any(a =>
            a.Target == "POWER_MODE" && a.Value is PowerModeState.Performance);

        if (isBatteryCritical && hasHighPowerAction)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"SAFETY VIOLATION: High power mode with critical battery");

            return false;
        }

        return true;
    }
}
