using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Services;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Power Agent - Intelligent power envelope management
/// Integrates existing PowerUsagePredictor with battery life optimization
/// Coordinates PL1/PL2/PL4 with thermal and battery constraints
/// ENHANCED: Elite hardware control via EliteFeaturesManager
/// Phase 4: CPU per-core and memory power management
/// </summary>
public class PowerAgent : IOptimizationAgent
{
    private readonly PowerUsagePredictor _powerPredictor;
    private readonly BatteryLifeEstimator _batteryEstimator;
    private readonly PowerModeFeature _powerModeFeature;
    private readonly EliteFeaturesManager? _eliteFeaturesManager;
    private readonly CPUCoreManager? _cpuCoreManager;
    private readonly MemoryPowerManager? _memoryPowerManager;
    private readonly PCIePowerManager? _pciePowerManager;

    public string AgentName => "PowerAgent";
    public AgentPriority Priority => AgentPriority.High;

    public PowerAgent(
        PowerUsagePredictor powerPredictor,
        BatteryLifeEstimator batteryEstimator,
        PowerModeFeature powerModeFeature,
        EliteFeaturesManager? eliteFeaturesManager = null,
        CPUCoreManager? cpuCoreManager = null,
        MemoryPowerManager? memoryPowerManager = null,
        PCIePowerManager? pciePowerManager = null)
    {
        _powerPredictor = powerPredictor ?? throw new ArgumentNullException(nameof(powerPredictor));
        _batteryEstimator = batteryEstimator ?? throw new ArgumentNullException(nameof(batteryEstimator));
        _powerModeFeature = powerModeFeature ?? throw new ArgumentNullException(nameof(powerModeFeature));
        _eliteFeaturesManager = eliteFeaturesManager; // Optional - graceful degradation
        _cpuCoreManager = cpuCoreManager; // Optional - Phase 4 elite feature
        _memoryPowerManager = memoryPowerManager; // Optional - Phase 4 elite feature
        _pciePowerManager = pciePowerManager; // Optional - Phase 4 PCIe/NVMe optimization
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

        // PRIORITY 1: Media Playback Optimization (ELITE power saving)
        if (context.CurrentWorkload.Type == WorkloadType.MediaPlayback)
        {
            HandleMediaPlaybackPower(proposal, context);
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Media playback detected - activating elite power saving mode");
            return proposal; // Media playback overrides other power management
        }

        // PRIORITY 2: Video Conferencing (Balanced approach)
        if (context.CurrentWorkload.Type == WorkloadType.VideoConferencing)
        {
            HandleVideoConferencingPower(proposal, context);
            return proposal;
        }

        // PRIORITY 3: Compilation (Short CPU burst optimization)
        if (context.CurrentWorkload.Type == WorkloadType.Compilation)
        {
            HandleCompilationPower(proposal, context);
            return proposal;
        }

        // STANDARD: Battery-aware power management
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

            // ML Learning: Track power optimization outcomes for future predictions
            // This data helps the power predictor learn optimal power profiles
            try
            {
                var powerSavings = powerBefore - powerAfter;
                var efficiency = powerSavings > 0 ? (powerSavings / (double)powerBefore) * 100 : 0;

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Power optimization: {powerSavings}W saved ({efficiency:F1}% efficiency)");

                // Update power predictor with actual results for learning
                _powerPredictor.UpdatePredictionAccuracy(result.ContextBefore, result.ContextAfter, powerBefore, powerAfter);
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Failed to update ML model with power outcome", ex);
            }
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

            // Phase 4: Aggressive CPU core parking + memory management
            // Productivity Mode Override: Apply even stricter power targets
            if (FeatureFlags.UseProductivityMode)
            {
                if (_cpuCoreManager != null)
                {
                    // Productivity: Park P-cores aggressively, prefer E-cores
                    var profile = CoreParkingProfile.MaximumPowerSaving;
                    await _cpuCoreManager.ApplyCoreParkingProfileAsync(profile, "Productivity Mode: E-core preference for battery life").ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Productivity Mode: Applied aggressive core parking: {profile}");
                }

                if (_memoryPowerManager != null)
                {
                    // Productivity: Use intelligent profiling based on actual memory state
                    var profile = _memoryPowerManager.GetOptimalProfile(
                        isOnBattery: true,
                        batteryPercent: batteryPercent,
                        availableMemoryMB: context.MemoryState.AvailableMemoryMB,
                        totalMemoryMB: context.MemoryState.TotalMemoryMB,
                        isIdle: context.CurrentWorkload.Type == WorkloadType.Idle);

                    await _memoryPowerManager.ApplyMemoryProfileAsync(profile, "Productivity Mode: Intelligent memory compression for battery").ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Productivity Mode: Applied memory profile: {profile}");
                }

                // Apply workload-aware NVMe power states
                if (_pciePowerManager != null)
                {
                    _pciePowerManager.ApplyWorkloadAwareNVMeStates(
                        context.CurrentWorkload.Type,
                        isOnBattery: true,
                        batteryPercent: batteryPercent);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Productivity Mode: Applied workload-aware NVMe states");
                }
            }
            else
            {
                // Standard Mode: Battery critical handling
                if (_cpuCoreManager != null)
                {
                    var profile = CoreParkingProfile.MaximumPowerSaving;
                    await _cpuCoreManager.ApplyCoreParkingProfileAsync(profile, "Battery critical - minimize active cores").ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Applied core parking: {profile} (battery critical)");
                }

                if (_memoryPowerManager != null)
                {
                    var profile = _memoryPowerManager.GetOptimalProfile(
                        isOnBattery: true,
                        batteryPercent: batteryPercent,
                        availableMemoryMB: context.MemoryState.AvailableMemoryMB,
                        totalMemoryMB: context.MemoryState.TotalMemoryMB,
                        isIdle: context.CurrentWorkload.Type == WorkloadType.Idle);

                    await _memoryPowerManager.ApplyMemoryProfileAsync(profile, "Battery critical - intelligent compression").ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Applied memory profile: {profile} (battery critical)");
                }

                // Apply workload-aware NVMe power states for battery critical
                if (_pciePowerManager != null)
                {
                    _pciePowerManager.ApplyWorkloadAwareNVMeStates(
                        context.CurrentWorkload.Type,
                        isOnBattery: true,
                        batteryPercent: batteryPercent);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Applied workload-aware NVMe states (battery critical)");
                }
            }
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

                // Phase 4: Power saving core parking + memory management
                if (_cpuCoreManager != null)
                {
                    var cpuUsage = context.CurrentWorkload.CpuUtilizationPercent;
                    var isThermalHigh = context.ThermalState.CpuTemp >= 85 || context.ThermalState.GpuTemp >= 78;
                    var profile = _cpuCoreManager.GetOptimalProfile(true, cpuUsage, batteryPercent, isThermalHigh);

                    await _cpuCoreManager.ApplyCoreParkingProfileAsync(profile, $"Battery {batteryPercent}% - proactive power saving").ConfigureAwait(false);
                }

                if (_memoryPowerManager != null)
                {
                    var profile = _memoryPowerManager.GetOptimalProfile(
                        isOnBattery: true,
                        batteryPercent: batteryPercent,
                        availableMemoryMB: context.MemoryState.AvailableMemoryMB,
                        totalMemoryMB: context.MemoryState.TotalMemoryMB,
                        isIdle: context.CurrentWorkload.Type == WorkloadType.Idle);

                    await _memoryPowerManager.ApplyMemoryProfileAsync(profile, "Battery preservation - intelligent compression").ConfigureAwait(false);
                }
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

            // Phase 4: Apply balanced profiles on battery
            if (_cpuCoreManager != null && batteryPercent < 30)
            {
                var profile = CoreParkingProfile.PowerSaving;
                await _cpuCoreManager.ApplyCoreParkingProfileAsync(profile, "Low battery - reduce active cores").ConfigureAwait(false);
            }

            if (_memoryPowerManager != null && batteryPercent < 30)
            {
                var profile = _memoryPowerManager.GetOptimalProfile(
                    isOnBattery: true,
                    batteryPercent: batteryPercent,
                    availableMemoryMB: context.MemoryState.AvailableMemoryMB,
                    totalMemoryMB: context.MemoryState.TotalMemoryMB,
                    isIdle: context.CurrentWorkload.Type == WorkloadType.Idle);

                await _memoryPowerManager.ApplyMemoryProfileAsync(profile, "Low battery - intelligent compression").ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Handle power management on AC power
    /// </summary>
    private async Task HandleACPowerAsync(AgentProposal proposal, SystemContext context)
    {
        var thermalHeadroom = 95 - context.ThermalState.CpuTemp;
        var cpuUsage = context.CurrentWorkload.CpuUtilizationPercent;

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

            // Phase 4: Performance core parking + memory profile
            if (_cpuCoreManager != null)
            {
                var profile = CoreParkingProfile.Performance;
                await _cpuCoreManager.ApplyCoreParkingProfileAsync(profile, "AC power - all cores active for performance").ConfigureAwait(false);
            }

            if (_memoryPowerManager != null)
            {
                var profile = _memoryPowerManager.GetOptimalProfile(
                    isOnBattery: false,
                    batteryPercent: context.BatteryState.ChargePercent,
                    availableMemoryMB: context.MemoryState.AvailableMemoryMB,
                    totalMemoryMB: context.MemoryState.TotalMemoryMB,
                    isIdle: context.CurrentWorkload.Type == WorkloadType.Idle);

                await _memoryPowerManager.ApplyMemoryProfileAsync(profile, "AC power - intelligent performance").ConfigureAwait(false);
            }
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

            // Phase 4: Balanced profiles on AC
            if (_cpuCoreManager != null)
            {
                var profile = cpuUsage > 60 ? CoreParkingProfile.Performance : CoreParkingProfile.Balanced;
                await _cpuCoreManager.ApplyCoreParkingProfileAsync(profile, "AC power - balanced mode").ConfigureAwait(false);
            }

            if (_memoryPowerManager != null)
            {
                var profile = _memoryPowerManager.GetOptimalProfile(
                    isOnBattery: false,
                    batteryPercent: context.BatteryState.ChargePercent,
                    availableMemoryMB: context.MemoryState.AvailableMemoryMB,
                    totalMemoryMB: context.MemoryState.TotalMemoryMB,
                    isIdle: context.CurrentWorkload.Type == WorkloadType.Idle);

                await _memoryPowerManager.ApplyMemoryProfileAsync(profile, "AC power - intelligent balanced").ConfigureAwait(false);
            }
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

            // Phase 4: Power saving on AC due to thermal constraints
            if (_cpuCoreManager != null)
            {
                var profile = CoreParkingProfile.PowerSaving;
                await _cpuCoreManager.ApplyCoreParkingProfileAsync(profile, "AC power - thermal limit, reduce active cores").ConfigureAwait(false);
            }

            if (_memoryPowerManager != null)
            {
                var profile = _memoryPowerManager.GetOptimalProfile(
                    isOnBattery: false,
                    batteryPercent: context.BatteryState.ChargePercent,
                    availableMemoryMB: context.MemoryState.AvailableMemoryMB,
                    totalMemoryMB: context.MemoryState.TotalMemoryMB,
                    isIdle: context.CurrentWorkload.Type == WorkloadType.Idle);

                await _memoryPowerManager.ApplyMemoryProfileAsync(profile, "AC power - thermal constraint").ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// ELITE POWER SAVING: Media Playback Optimization
    /// Target: 85-90% power reduction vs gaming (175W -> 18-25W)
    /// Video decode requires minimal CPU/GPU - maximize battery life
    /// ENHANCED: Triggers EliteFeaturesManager for OS-level + hardware-level control
    /// </summary>
    private void HandleMediaPlaybackPower(AgentProposal proposal, SystemContext context)
    {
        // LAYER 1: Trigger Elite Features Manager (if available)
        // This handles: Process priority, Windows power, MSR, NVAPI, PCIe ASPM
        if (_eliteFeaturesManager != null)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "ELITE_PROFILE",
                Value = ElitePowerProfile.MediaPlayback,
                Reason = "Media playback: Activate elite power saving (MSR/NVAPI/PCIe/Process/Windows)",
                Context = context,
                Parameters = new Dictionary<string, object>
                {
                    ["EliteFeaturesManager"] = _eliteFeaturesManager,
                    ["AsyncAction"] = true // Signals executor to await async method
                }
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Elite Features Manager triggered for media playback (OS + hardware control)");
        }

        // LAYER 2: EC-Level CPU POWER REDUCTION
        // Video decode typically needs 10-15W, we set conservative 20W
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "CPU_PL1",
            Value = 20, // Down from 55W (64% reduction)
            Reason = "Media playback: video decode requires minimal sustained power",
            Context = context
        });

        // DISABLE TURBO BOOST (not needed for video decode)
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "CPU_PL2",
            Value = 25, // Down from 115W (78% reduction) - basically disabled
            Reason = "Media playback: no CPU bursts needed for video decode",
            Context = context
        });

        // AGGRESSIVE PL4 REDUCTION
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "CPU_PL4",
            Value = 30, // Down from 175W (83% reduction)
            Reason = "Media playback: no power spikes needed",
            Context = context
        });

        // QUIET POWER MODE (minimal fan noise for movie watching)
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "POWER_MODE",
            Value = PowerModeState.Quiet,
            Reason = "Media playback: prioritize silent operation",
            Context = context
        });

        // GPU POWER REDUCTION (recommend iGPU, but GPUAgent handles actual switching)
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Opportunistic,
            Target = "GPU_TGP",
            Value = 0, // Signal to GPUAgent: disable dGPU if possible
            Reason = "Media playback: Intel QuickSync (iGPU) more efficient than NVIDIA",
            Context = context
        });

        // LAYER 3: ELITE STORAGE POWER SAVING
        // Media playback: Minimal disk access after buffering (PS3 = 0.1-0.3W vs PS0 = 3-8W)
        if (_pciePowerManager != null)
        {
            _pciePowerManager.ApplyMediaPlaybackProfile();

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Media playback: PCIe ASPM + NVMe PS3 (target: 5-8W storage savings)");
        }

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Media playback power optimization: PL1=20W, PL2=25W (turbo disabled)");
            Log.Instance.Trace($"Target total system power: 18-25W (85-90% reduction from gaming)");
        }
    }

    /// <summary>
    /// Video Conferencing Optimization
    /// Balance: Need CPU for encoding, but can reduce GPU and maintain quality
    /// </summary>
    private void HandleVideoConferencingPower(AgentProposal proposal, SystemContext context)
    {
        // Moderate CPU power (encoding needs CPU)
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Opportunistic,
            Target = "CPU_PL1",
            Value = 35, // Moderate reduction (video encoding needs CPU)
            Reason = "Video conferencing: CPU needed for video/audio encoding",
            Context = context
        });

        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Opportunistic,
            Target = "CPU_PL2",
            Value = 60, // Allow some turbo for encoding spikes
            Reason = "Video conferencing: allow bursts for encoding",
            Context = context
        });

        // Minimal GPU (unless using NVENC for encoding)
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Opportunistic,
            Target = "GPU_TGP",
            Value = 40, // Minimal dGPU power
            Reason = "Video conferencing: iGPU or minimal dGPU",
            Context = context
        });

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Video conferencing optimization: balanced CPU/GPU for encoding");
    }

    /// <summary>
    /// Compilation Workload Optimization
    /// Short CPU bursts - allow high power for faster compilation
    /// </summary>
    private void HandleCompilationPower(AgentProposal proposal, SystemContext context)
    {
        // HIGH CPU POWER (compilation benefits from high clocks)
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Opportunistic,
            Target = "CPU_PL1",
            Value = 65, // High sustained power for compilation
            Reason = "Compilation: maximize CPU for faster builds",
            Context = context
        });

        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Opportunistic,
            Target = "CPU_PL2",
            Value = 140, // Maximum turbo for single-threaded compilation phases
            Reason = "Compilation: allow turbo for burst performance",
            Context = context
        });

        // MINIMAL GPU (compilation doesn't use GPU)
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Opportunistic,
            Target = "GPU_TGP",
            Value = 40, // Minimal dGPU
            Reason = "Compilation: CPU-only workload",
            Context = context
        });

        // Performance mode for compilation
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Opportunistic,
            Target = "POWER_MODE",
            Value = PowerModeState.Performance,
            Reason = "Compilation: prioritize build speed",
            Context = context
        });

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Compilation optimization: maximum CPU performance");
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
    private const int SmoothedSampleSize = 30; // 30 seconds for smoothed average

    /// <summary>
    /// Estimate time remaining based on smoothed discharge rate and predicted workload
    /// </summary>
    public TimeSpan EstimateTimeRemaining(SystemContext context)
    {
        if (!context.BatteryState.IsOnBattery)
            return TimeSpan.MaxValue;

        var chargePercent = context.BatteryState.ChargePercent;

        // Use smoothed discharge rate instead of instant reading
        var dischargeRateMw = GetSmoothedDischargeRate(context);

        if (dischargeRateMw <= 0)
            return context.BatteryState.EstimatedTimeRemaining;

        // Calculate based on smoothed rate with correct units
        var capacityMwh = context.BatteryState.FullChargeCapacityMwh;
        if (capacityMwh <= 0)
            return TimeSpan.Zero;

        var remainingMwh = (capacityMwh * chargePercent) / 100.0;

        // Predict future discharge rate based on workload and optimizations
        var predictedDischargeRateMw = PredictFutureDischargeRate(context, dischargeRateMw);

        // Calculate hours: Energy (mWh) / Power (mW) = hours
        var hoursRemaining = remainingMwh / predictedDischargeRateMw;

        // Clamp to reasonable range (0 to 24 hours)
        hoursRemaining = Math.Max(0, Math.Min(hoursRemaining, 24));

        return TimeSpan.FromHours(hoursRemaining);
    }

    /// <summary>
    /// Get smoothed discharge rate over recent history to avoid erratic estimates
    /// </summary>
    private double GetSmoothedDischargeRate(SystemContext context)
    {
        var currentRate = Math.Abs(context.BatteryState.ChargeRateMw);

        // Record current data point
        RecordBatteryDataPoint(context);

        // If insufficient history, use current rate
        if (_history.Count < 5)
            return Math.Max(currentRate, 1000); // Minimum 1W to avoid division by zero

        // Calculate weighted average (recent samples weighted higher)
        var recentSamples = _history.TakeLast(Math.Min(SmoothedSampleSize, _history.Count)).ToList();
        if (recentSamples.Count == 0)
            return Math.Max(currentRate, 1000);

        double weightedSum = 0;
        double weightTotal = 0;

        for (int i = 0; i < recentSamples.Count; i++)
        {
            var weight = i + 1; // Recent samples have higher weight
            var rate = Math.Abs(recentSamples[i].DischargeRateMw);
            weightedSum += rate * weight;
            weightTotal += weight;
        }

        var smoothedRate = weightTotal > 0 ? weightedSum / weightTotal : currentRate;

        // Ensure minimum rate to avoid division by zero
        return Math.Max(smoothedRate, 1000); // Minimum 1W
    }

    /// <summary>
    /// Predict future discharge rate based on workload and active optimizations
    /// </summary>
    private double PredictFutureDischargeRate(SystemContext context, double currentSmoothedRate)
    {
        var predictedRate = currentSmoothedRate;

        // Adjust based on workload type
        switch (context.CurrentWorkload.Type)
        {
            case WorkloadType.Gaming:
                // Gaming: Higher sustained power (if not already gaming)
                if (_history.Count > 0 && _history.Last().WorkloadType != WorkloadType.Gaming)
                    predictedRate = Math.Max(predictedRate, 80000); // 80W minimum for gaming
                break;

            case WorkloadType.MediaPlayback:
                // Media playback with elite optimizations: 18-25W
                predictedRate = Math.Min(predictedRate, 25000); // Cap at 25W for media
                if (predictedRate > 30000) // If currently high, predict drop
                    predictedRate = 20000; // Target 20W
                break;

            case WorkloadType.LightProductivity:
            case WorkloadType.HeavyProductivity:
                // Productivity: 30-50W typical
                predictedRate = Math.Max(Math.Min(predictedRate, 50000), 25000);
                break;

            case WorkloadType.Idle:
                // Idle with optimizations: 10-15W
                predictedRate = Math.Min(predictedRate, 15000);
                if (predictedRate > 20000) // If currently high, predict drop
                    predictedRate = 12000; // Target 12W
                break;
        }

        // Adjust for user intent
        if (context.UserIntent == UserIntent.BatterySaving)
        {
            // Battery saving mode: reduce predicted rate by 30%
            predictedRate *= 0.7;
        }
        else if (context.UserIntent == UserIntent.MaxPerformance)
        {
            // Max performance: increase predicted rate by 20%
            predictedRate *= 1.2;
        }

        return Math.Max(predictedRate, 1000); // Minimum 1W
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
