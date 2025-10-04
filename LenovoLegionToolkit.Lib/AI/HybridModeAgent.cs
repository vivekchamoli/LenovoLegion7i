using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Hybrid Mode Agent - Intelligent GPU switching for battery optimization
/// Automatically switches between iGPU and dGPU based on workload
/// Priority: High (30-40% battery impact)
/// </summary>
public class HybridModeAgent : IOptimizationAgent
{
    private readonly HybridModeFeature _hybridModeFeature;
    private HybridModeState? _previousMode;

    public string AgentName => "HybridModeAgent";
    public AgentPriority Priority => AgentPriority.High;

    public HybridModeAgent(HybridModeFeature hybridModeFeature)
    {
        _hybridModeFeature = hybridModeFeature ?? throw new ArgumentNullException(nameof(hybridModeFeature));
    }

    public async Task<AgentProposal> ProposeActionsAsync(SystemContext context)
    {
        var proposal = new AgentProposal
        {
            Agent = AgentName,
            Priority = Priority
        };

        // Skip if hybrid mode not supported
        if (!await _hybridModeFeature.IsSupportedAsync().ConfigureAwait(false))
            return proposal;

        var currentMode = await _hybridModeFeature.GetStateAsync().ConfigureAwait(false);
        var targetMode = DetermineOptimalGPUMode(context, currentMode);

        if (targetMode != currentMode)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = GetActionType(context),
                Target = "GPU_HYBRID_MODE",
                Value = targetMode,
                Reason = GetSwitchReason(context, currentMode, targetMode)
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU mode switch recommended: {currentMode} → {targetMode}");
        }

        return proposal;
    }

    public Task OnActionsExecutedAsync(ExecutionResult result)
    {
        // Track GPU mode changes for learning
        if (result.Success && result.ExecutedActions.Count > 0)
        {
            foreach (var action in result.ExecutedActions)
            {
                if (action.Target == "GPU_HYBRID_MODE" && action.Value is HybridModeState mode)
                {
                    _previousMode = mode;

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"GPU mode changed to {mode}");
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Determine optimal GPU mode based on context
    /// </summary>
    private HybridModeState DetermineOptimalGPUMode(SystemContext context, HybridModeState currentMode)
    {
        // CRITICAL BATTERY: Always use iGPU only
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 15)
        {
            return HybridModeState.OnIGPUOnly;
        }

        // ON AC POWER: Use based on workload
        if (!context.BatteryState.IsOnBattery)
        {
            return context.UserIntent switch
            {
                UserIntent.Gaming => HybridModeState.Off, // dGPU always on for gaming
                UserIntent.MaxPerformance => HybridModeState.Off, // dGPU for max performance
                UserIntent.Productivity => HybridModeState.On, // Hybrid for flexibility
                UserIntent.Balanced => HybridModeState.OnAuto, // Auto-switch
                _ => HybridModeState.OnAuto // Default: auto-switch
            };
        }

        // ON BATTERY: Optimize for battery life
        return context.UserIntent switch
        {
            // Gaming on battery: Use hybrid but warn user
            UserIntent.Gaming when context.BatteryState.ChargePercent > 50 => HybridModeState.On,
            UserIntent.Gaming => HybridModeState.OnIGPUOnly, // Low battery: force iGPU

            // Max performance: Use hybrid if battery permits
            UserIntent.MaxPerformance when context.BatteryState.ChargePercent > 40 => HybridModeState.On,
            UserIntent.MaxPerformance => HybridModeState.OnIGPUOnly,

            // Battery saving mode: Always iGPU
            UserIntent.BatterySaving => HybridModeState.OnIGPUOnly,

            // Everything else: iGPU only for maximum battery
            _ => HybridModeState.OnIGPUOnly
        };
    }

    /// <summary>
    /// Get action type based on context urgency
    /// </summary>
    private ActionType GetActionType(SystemContext context)
    {
        // Critical battery: must switch immediately
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 15)
            return ActionType.Critical;

        // Low battery: proactive switching
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 30)
            return ActionType.Proactive;

        // Otherwise opportunistic
        return ActionType.Opportunistic;
    }

    /// <summary>
    /// Get human-readable reason for GPU mode switch
    /// </summary>
    private string GetSwitchReason(SystemContext context, HybridModeState from, HybridModeState to)
    {
        if (context.BatteryState.IsOnBattery)
        {
            if (context.BatteryState.ChargePercent < 15)
                return $"Critical battery ({context.BatteryState.ChargePercent}%) - switching to iGPU only";

            if (context.BatteryState.ChargePercent < 30)
                return $"Low battery ({context.BatteryState.ChargePercent}%) - optimizing for battery life";

            return $"On battery - switching to {to} for {context.UserIntent} workload";
        }

        return $"AC power - optimizing for {context.UserIntent} workload";
    }
}
