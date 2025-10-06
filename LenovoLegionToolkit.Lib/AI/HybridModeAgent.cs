using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Services;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Hybrid Mode Agent - Intelligent GPU switching for battery optimization
/// Automatically switches between iGPU and dGPU based on workload
/// Priority: High (30-40% battery impact)
/// Phase 1 Fix: Uses GPUTransitionManager for thread-safe transitions with cost awareness
/// </summary>
public class HybridModeAgent : IOptimizationAgent
{
    private readonly HybridModeFeature _hybridModeFeature;
    private readonly GPUTransitionManager _transitionManager;
    private readonly DisplayTopologyService _displayTopologyService;
    private readonly ProcessLaunchMonitor? _processLaunchMonitor;
    private readonly BatteryStateService? _batteryStateService;
    private HybridModeState? _previousMode;
    private ProcessLaunchPrediction? _pendingPrediction;

    public string AgentName => "HybridModeAgent";
    public AgentPriority Priority => AgentPriority.High;

    public HybridModeAgent(
        HybridModeFeature hybridModeFeature,
        GPUTransitionManager transitionManager,
        DisplayTopologyService displayTopologyService,
        ProcessLaunchMonitor? processLaunchMonitor = null,
        BatteryStateService? batteryStateService = null)
    {
        _hybridModeFeature = hybridModeFeature ?? throw new ArgumentNullException(nameof(hybridModeFeature));
        _transitionManager = transitionManager ?? throw new ArgumentNullException(nameof(transitionManager));
        _displayTopologyService = displayTopologyService ?? throw new ArgumentNullException(nameof(displayTopologyService));
        _processLaunchMonitor = processLaunchMonitor;
        _batteryStateService = batteryStateService;

        // Subscribe to process launch predictions (Phase 2: Predictive switching)
        if (_processLaunchMonitor != null)
        {
            _processLaunchMonitor.ProcessLaunched += OnProcessLaunched;
        }
    }

    /// <summary>
    /// Handle predictive process launch (Phase 2: Predictive Intelligence)
    /// Switches GPU mode BEFORE workload is detected
    /// </summary>
    private void OnProcessLaunched(object? sender, ProcessLaunchPrediction prediction)
    {
        // Store prediction for next ProposeActionsAsync cycle
        _pendingPrediction = prediction;

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Predictive switch queued: {prediction.ProcessName} → {prediction.RecommendedMode} (confidence: {prediction.Confidence}%, requirement: {prediction.Requirement})");
        }
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

        // PHASE 1 FIX: Thread-safe state retrieval with no race condition
        var currentMode = await _transitionManager.GetCurrentStateAsync().ConfigureAwait(false);

        // PHASE 2 FIX: Check for predictive process launch FIRST
        HybridModeState targetMode;
        string decisionReason;
        bool isPredictive = false;

        if (_pendingPrediction != null && IsHighConfidencePrediction(_pendingPrediction))
        {
            // PREDICTIVE PATH: Process launch detected, switch before workload appears
            targetMode = _pendingPrediction.RecommendedMode;
            decisionReason = $"Predictive switch for {_pendingPrediction.ProcessName} " +
                $"({_pendingPrediction.Confidence}% confidence)";
            isPredictive = true;

            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"Using predictive GPU mode: {targetMode} for {_pendingPrediction.ProcessName}");
            }

            // Clear prediction after use
            _pendingPrediction = null;
        }
        else
        {
            // REACTIVE PATH: Standard workload-based decision
            targetMode = await DetermineOptimalGPUModeAsync(context, currentMode).ConfigureAwait(false);
            decisionReason = GetSwitchReason(context, currentMode, targetMode);
        }

        if (targetMode != currentMode)
        {
            // PHASE 1 FIX: Check transition cost and dwell time before proposing
            // PHASE 2: Predictive switches get High priority (skip some dwell time)
            var transitionPriority = isPredictive ? TransitionPriority.High : GetTransitionPriority(context);

            var transitionProposal = await _transitionManager.ProposeTransitionAsync(
                targetMode,
                decisionReason,
                transitionPriority).ConfigureAwait(false);

            if (transitionProposal != null && !transitionProposal.IsBlocked)
            {
                proposal.Actions.Add(new ResourceAction
                {
                    Type = isPredictive ? ActionType.Proactive : GetActionType(context),
                    Target = "GPU_HYBRID_MODE",
                    Value = transitionProposal, // Pass the full proposal for execution
                    Reason = decisionReason,
                    Context = context
                });

                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"GPU mode switch recommended: {currentMode} → {targetMode} (cost: {transitionProposal.EstimatedCost.TotalSeconds:F1}s, priority: {transitionPriority})");
                }
            }
            else if (transitionProposal?.IsBlocked == true && Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"GPU mode switch blocked: {transitionProposal.BlockReason} (remaining: {transitionProposal.RemainingDwellTime?.TotalSeconds:F0}s)");
            }
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
    /// Enhanced with workload-specific GPU switching (media playback, video conferencing)
    /// Phase 1 Fix: Checks display topology before iGPU-only mode
    /// Phase 3: Thermal feedback integration
    /// Work Mode: Force iGPU for maximum battery life (8-10 hours target)
    /// </summary>
    private async Task<HybridModeState> DetermineOptimalGPUModeAsync(SystemContext context, HybridModeState currentMode)
    {
        // PHASE 1 FIX: Check display topology first - CRITICAL SAFETY CHECK
        // If external display is on dGPU, NEVER switch to iGPU-only (would blank display)
        var isIGPUOnlySafe = await _displayTopologyService.IsIGPUOnlyModeSafeAsync().ConfigureAwait(false);

        // WORK MODE (PRODUCTIVITY): Force iGPU for office/professional workflows
        // Target: 8-10h battery life, RTX 4070 stays in D3Cold (0W), Intel Iris Xe handles everything
        // Savings: ~40W when media/browsing vs dGPU active
        if (FeatureFlags.UseProductivityMode)
        {
            if (!isIGPUOnlySafe)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Work Mode: iGPU-only blocked (external display on dGPU), using Hybrid as fallback");
                return HybridModeState.On; // Fallback to hybrid mode for safety
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Work Mode: Forcing iGPU-only for office/professional workflow (40W savings)");

            return HybridModeState.OnIGPUOnly; // Force iGPU for productivity
        }

        // PHASE 3: Thermal feedback - Prevent GPU usage when temperatures critical
        var isThermalCritical = context.ThermalState.CpuTemp >= 90 || context.ThermalState.GpuTemp >= 83;
        var isThermalHigh = context.ThermalState.CpuTemp >= 85 || context.ThermalState.GpuTemp >= 78;

        if (isThermalCritical && isIGPUOnlySafe)
        {
            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"THERMAL CRITICAL: CPU={context.ThermalState.CpuTemp}°C GPU={context.ThermalState.GpuTemp}°C - forcing iGPU-only to reduce heat");
            }
            return HybridModeState.OnIGPUOnly; // Override everything - thermal safety first
        }

        // PRIORITY 1: Media Playback - FORCE iGPU only (Intel QuickSync >> NVIDIA for power)
        // Intel hardware video decode uses ~2-5W vs NVIDIA idle ~10-15W
        if (context.CurrentWorkload.Type == WorkloadType.MediaPlayback)
        {
            if (!isIGPUOnlySafe)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Media playback: iGPU-only blocked (external display on dGPU), using Hybrid");
                return HybridModeState.On; // Fallback to hybrid mode
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Media playback: Forcing iGPU-only mode (Intel QuickSync for video decode)");

            return HybridModeState.OnIGPUOnly; // 10-20W savings vs dGPU
        }

        // PRIORITY 2: Video Conferencing - Use iGPU (QuickSync good for encoding)
        if (context.CurrentWorkload.Type == WorkloadType.VideoConferencing)
        {
            if (!isIGPUOnlySafe)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Video conferencing: iGPU-only blocked (external display on dGPU), using Hybrid");
                return HybridModeState.On; // Fallback to hybrid mode
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Video conferencing: Forcing iGPU-only mode (Intel QuickSync for encoding)");

            return HybridModeState.OnIGPUOnly;
        }

        // PRIORITY 3: Productivity workloads - Prefer iGPU on battery
        if (context.CurrentWorkload.Type == WorkloadType.LightProductivity ||
            context.CurrentWorkload.Type == WorkloadType.HeavyProductivity)
        {
            if (context.BatteryState.IsOnBattery)
            {
                if (!isIGPUOnlySafe)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Productivity: iGPU-only blocked (external display on dGPU), using Hybrid");
                    return HybridModeState.On;
                }
                return HybridModeState.OnIGPUOnly; // Save battery for productivity
            }
        }

        // CRITICAL BATTERY: Always use iGPU only (unless external display on dGPU)
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 15)
        {
            if (!isIGPUOnlySafe)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Critical battery: iGPU-only blocked (external display on dGPU), using Hybrid");
                return HybridModeState.On; // Better than blanking the display!
            }
            return HybridModeState.OnIGPUOnly;
        }

        // PHASE 2: Discharge rate-aware GPU switching
        if (context.BatteryState.IsOnBattery && _batteryStateService != null)
        {
            var dischargeLevel = _batteryStateService.GetDischargeRateLevel();

            // Critical discharge (> 50W) - force iGPU only to extend battery
            if (dischargeLevel == DischargeRateLevel.Critical && isIGPUOnlySafe)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Critical discharge rate ({dischargeLevel}) - forcing iGPU-only to extend battery");
                return HybridModeState.OnIGPUOnly;
            }

            // High discharge (30-50W) with medium battery - prefer iGPU
            if (dischargeLevel == DischargeRateLevel.High &&
                context.BatteryState.ChargePercent < 50 &&
                isIGPUOnlySafe)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"High discharge rate ({dischargeLevel}) + {context.BatteryState.ChargePercent}% battery - preferring iGPU-only");
                return HybridModeState.OnIGPUOnly;
            }
        }

        // ON AC POWER: Use based on workload (with thermal limits - Phase 3)
        if (!context.BatteryState.IsOnBattery)
        {
            // PHASE 3: High thermal - prefer hybrid/auto even on AC
            if (isThermalHigh && isIGPUOnlySafe)
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Thermal high on AC: CPU={context.ThermalState.CpuTemp}°C GPU={context.ThermalState.GpuTemp}°C - using Hybrid/iGPU to cool down");
                }

                return context.UserIntent switch
                {
                    UserIntent.Gaming => HybridModeState.On, // Hybrid instead of always-dGPU
                    UserIntent.MaxPerformance => HybridModeState.On, // Hybrid to reduce heat
                    _ => HybridModeState.OnIGPUOnly // Others use iGPU for cooling
                };
            }

            // Normal AC power operation
            return context.UserIntent switch
            {
                UserIntent.Gaming => HybridModeState.Off, // dGPU always on for gaming
                UserIntent.MaxPerformance => HybridModeState.Off, // dGPU for max performance
                UserIntent.Productivity => HybridModeState.On, // Hybrid for flexibility
                UserIntent.Balanced => HybridModeState.OnAuto, // Auto-switch
                _ => HybridModeState.OnAuto // Default: auto-switch
            };
        }

        // ON BATTERY: Optimize for battery life (with display topology safety)
        var batteryModeDecision = context.UserIntent switch
        {
            // Gaming on battery: Use hybrid but warn user
            UserIntent.Gaming when context.BatteryState.ChargePercent > 50 => HybridModeState.On,
            UserIntent.Gaming => HybridModeState.OnIGPUOnly, // Low battery: force iGPU (if safe)

            // Max performance: Use hybrid if battery permits
            UserIntent.MaxPerformance when context.BatteryState.ChargePercent > 40 => HybridModeState.On,
            UserIntent.MaxPerformance => HybridModeState.OnIGPUOnly, // Low battery (if safe)

            // Battery saving mode: Always iGPU (if safe)
            UserIntent.BatterySaving => HybridModeState.OnIGPUOnly,

            // Everything else: iGPU only for maximum battery (if safe)
            _ => HybridModeState.OnIGPUOnly
        };

        // PHASE 1 FIX: Final safety check - if decision is iGPU-only, verify it's safe
        if (batteryModeDecision == HybridModeState.OnIGPUOnly && !isIGPUOnlySafe)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery mode: iGPU-only blocked (external display on dGPU), using Hybrid as fallback");
            return HybridModeState.On; // Fallback to hybrid
        }

        return batteryModeDecision;
    }

    /// <summary>
    /// Get transition priority based on context urgency (Phase 1 Fix)
    /// Maps to GPUTransitionManager priority levels
    /// </summary>
    private TransitionPriority GetTransitionPriority(SystemContext context)
    {
        // Critical battery: bypass dwell time
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 15)
            return TransitionPriority.Critical;

        // Low battery: high priority (reduced dwell time)
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 30)
            return TransitionPriority.High;

        // Otherwise normal (respects full dwell time)
        return TransitionPriority.Normal;
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

    /// <summary>
    /// Check if prediction is high confidence (Phase 2: Predictive Intelligence)
    /// Only act on high-confidence predictions to avoid false positives
    /// </summary>
    private bool IsHighConfidencePrediction(ProcessLaunchPrediction prediction)
    {
        // Require 70%+ confidence for predictive switches
        if (prediction.Confidence < 70)
            return false;

        // GPU-required apps: Always trust (games, 3D apps)
        if (prediction.Requirement == GPURequirement.Required)
            return true;

        // iGPU optimal: Trust if confidence > 80% (media players, browsers)
        if (prediction.Requirement == GPURequirement.IGPUOptimal && prediction.Confidence >= 80)
            return true;

        // GPU preferred: Trust if confidence > 85% (video editors)
        if (prediction.Requirement == GPURequirement.Preferred && prediction.Confidence >= 85)
            return true;

        return false;
    }
}
