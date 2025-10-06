using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.FanCurve;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Thermal Agent - Multi-horizon predictive thermal management
/// Prevents thermal throttling through proactive cooling adjustments
/// Horizons: 15s (emergency), 60s (proactive), 300s (strategic)
/// Includes adaptive fan curve learning for optimal thermal/acoustic balance
/// </summary>
public class ThermalAgent : IOptimizationAgent
{
    private readonly ThermalOptimizer _thermalOptimizer;
    private readonly SystemContextStore _contextStore;
    private readonly AdaptiveFanCurveController? _adaptiveFanController;
    private readonly DataPersistenceService? _persistenceService;

    // Multi-horizon prediction intervals
    private const int SHORT_HORIZON_SEC = 15;   // Emergency response
    private const int MEDIUM_HORIZON_SEC = 60;  // Proactive cooling
    private const int LONG_HORIZON_SEC = 300;   // Strategic adaptation

    // Rate limiting for emergency actions to prevent oscillation
    private DateTime _lastEmergencyAction = DateTime.MinValue;
    private const int EMERGENCY_COOLDOWN_SEC = 30;  // 30 second cooldown between emergency actions

    // Gen 9 thermal thresholds (Legion Slim 7i)
    private const int CPU_THROTTLE_TEMP = 95;   // CPU starts throttling
    private const int GPU_THROTTLE_TEMP = 87;   // GPU starts throttling
    private const int SAFE_CPU_TEMP = 85;       // Target safe temperature
    private const int SAFE_GPU_TEMP = 75;       // Target safe temperature

    public string AgentName => "ThermalAgent";
    public AgentPriority Priority => AgentPriority.Critical;

    public ThermalAgent(
        ThermalOptimizer thermalOptimizer,
        SystemContextStore contextStore,
        AdaptiveFanCurveController? adaptiveFanController = null,
        DataPersistenceService? persistenceService = null)
    {
        _thermalOptimizer = thermalOptimizer ?? throw new ArgumentNullException(nameof(thermalOptimizer));
        _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
        _adaptiveFanController = adaptiveFanController;
        _persistenceService = persistenceService;
    }

    public async Task<AgentProposal> ProposeActionsAsync(SystemContext context)
    {
        var proposal = new AgentProposal
        {
            Agent = AgentName,
            Priority = Priority,
            Metadata = new Dictionary<string, object>
            {
                ["ContextTimestamp"] = context.Timestamp,
                ["CpuTemp"] = context.ThermalState.CpuTemp,
                ["GpuTemp"] = context.ThermalState.GpuTemp
            }
        };

        // PRIORITY 0: WORK MODE (PRODUCTIVITY) - Silent Operation (<25dB target)
        // Office/professional workflows: Prioritize silence over aggressive cooling
        // Target: Whisper-quiet operation, accept higher temps (80°C vs 70°C)
        if (FeatureFlags.UseProductivityMode)
        {
            HandleWorkModeThermals(proposal, context);
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Work Mode: Silent thermal mode activated (<25dB target, passive cooling priority)");
            return proposal;
        }

        // PRIORITY 1: Media Playback - Silent Mode (acoustic comfort priority)
        if (context.CurrentWorkload.Type == WorkloadType.MediaPlayback)
        {
            HandleMediaPlaybackThermals(proposal, context);
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Media playback: Silent thermal mode activated (max 30% fan speed)");
            return proposal;
        }

        // Multi-horizon thermal predictions
        var predictions = await PredictMultiHorizonTemperaturesAsync(context).ConfigureAwait(false);

        // Log predictions for telemetry
        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Thermal predictions - Short: CPU={predictions.ShortHorizonCpuTemp:F1}°C GPU={predictions.ShortHorizonGpuTemp:F1}°C, Medium: CPU={predictions.MediumHorizonCpuTemp:F1}°C GPU={predictions.MediumHorizonGpuTemp:F1}°C");
        }

        // EMERGENCY ACTIONS (15-second horizon) - with rate limiting
        var timeSinceLastEmergency = (DateTime.UtcNow - _lastEmergencyAction).TotalSeconds;
        if ((predictions.ShortHorizonCpuTemp >= CPU_THROTTLE_TEMP - 3 ||
            predictions.ShortHorizonGpuTemp >= GPU_THROTTLE_TEMP - 3) &&
            timeSinceLastEmergency >= EMERGENCY_COOLDOWN_SEC)
        {
            AddEmergencyThermalActions(proposal, context, predictions);
            _lastEmergencyAction = DateTime.UtcNow;
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
        // VRM temps above 90°C can cause system instability and damage
        // Gen 9 VRM safe limit is 95°C, we act at 85°C for safety margin
        if (context.ThermalState.VrmTemp > 85)
        {
            if (context.ThermalState.VrmTemp > 90)
            {
                // CRITICAL: VRM near limit - emergency power reduction
                proposal.Actions.Add(new ResourceAction
                {
                    Type = ActionType.Emergency,
                    Target = "CPU_PL1",
                    Value = Math.Max(35, context.PowerState.CurrentPL1 - 20),
                    Reason = $"CRITICAL VRM temp: {context.ThermalState.VrmTemp}°C (emergency power reduction)"
                });

                // Also reduce PL2 to prevent burst loads
                proposal.Actions.Add(new ResourceAction
                {
                    Type = ActionType.Emergency,
                    Target = "CPU_PL2",
                    Value = Math.Max(80, context.PowerState.CurrentPL2 - 30),
                    Reason = $"CRITICAL VRM temp: {context.ThermalState.VrmTemp}°C (limiting burst power)"
                });
            }
            else
            {
                // WARNING: VRM elevated - proactive power reduction
                proposal.Actions.Add(new ResourceAction
                {
                    Type = ActionType.Proactive,
                    Target = "CPU_PL1",
                    Value = Math.Max(40, context.PowerState.CurrentPL1 - 15),
                    Reason = $"VRM elevated: {context.ThermalState.VrmTemp}°C (reducing sustained power)"
                });
            }

            // Increase fan speed to cool VRM area
            proposal.Actions.Add(new ResourceAction
            {
                Type = context.ThermalState.VrmTemp > 90 ? ActionType.Emergency : ActionType.Proactive,
                Target = "FAN_SPEED_CPU",
                Value = Math.Min(255, context.ThermalState.Fan1Speed + 40), // Increase by ~15%
                Reason = $"Cooling VRM: {context.ThermalState.VrmTemp}°C"
            });
        }

        // ADAPTIVE FAN CURVE LEARNING (if enabled)
        if (_adaptiveFanController != null && FeatureFlags.UseAdaptiveFanCurves)
        {
            // Get adaptive fan speed suggestions based on learned patterns
            var cpuFanSuggestion = _adaptiveFanController.SuggestFanSpeed(
                currentTemp: context.ThermalState.CpuTemp,
                currentFanSpeed: context.ThermalState.Fan1Speed * 100 / 255, // Convert 0-255 to percentage
                tempTrend: (int)context.ThermalState.Trend.CpuTrendPerSecond,
                powerMode: context.PowerState.CurrentPowerMode
            );

            if (cpuFanSuggestion.ShouldAdjust)
            {
                // Convert percentage (0-100) to hardware value (0-255)
                var fanSpeedValue = cpuFanSuggestion.RecommendedFanSpeed * 255 / 100;

                proposal.Actions.Add(new ResourceAction
                {
                    Type = ActionType.Proactive,
                    Target = "FAN_SPEED_CPU",
                    Value = fanSpeedValue,
                    Reason = $"Adaptive learning: {cpuFanSuggestion.Reason}"
                });

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Adaptive fan suggestion: CPU fan {cpuFanSuggestion.RecommendedFanSpeed}% - {cpuFanSuggestion.Reason}");
            }

            var gpuFanSuggestion = _adaptiveFanController.SuggestFanSpeed(
                currentTemp: context.ThermalState.GpuTemp,
                currentFanSpeed: context.ThermalState.Fan2Speed * 100 / 255,
                tempTrend: (int)context.ThermalState.Trend.GpuTrendPerSecond,
                powerMode: context.PowerState.CurrentPowerMode
            );

            if (gpuFanSuggestion.ShouldAdjust)
            {
                var fanSpeedValue = gpuFanSuggestion.RecommendedFanSpeed * 255 / 100;

                proposal.Actions.Add(new ResourceAction
                {
                    Type = ActionType.Proactive,
                    Target = "FAN_SPEED_GPU",
                    Value = fanSpeedValue,
                    Reason = $"Adaptive learning: {gpuFanSuggestion.Reason}"
                });

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Adaptive fan suggestion: GPU fan {gpuFanSuggestion.RecommendedFanSpeed}% - {gpuFanSuggestion.Reason}");
            }
        }

        return proposal;
    }

    public async Task OnActionsExecutedAsync(ExecutionResult result)
    {
        // Learn from thermal action outcomes
        if (result.Success && result.ExecutedActions.Any(a => a.Target.Contains("FAN") || a.Target.Contains("PL")))
        {
            var tempBefore = result.ContextBefore.ThermalState.CpuTemp;
            var tempAfter = result.ContextAfter.ThermalState.CpuTemp;
            var tempDelta = tempAfter - tempBefore;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Thermal action result: {tempBefore}°C -> {tempAfter}°C (Δ={tempDelta:+0;-0}°C)");

            // Record thermal performance for adaptive fan curve learning
            if (_adaptiveFanController != null && FeatureFlags.UseAdaptiveFanCurves)
            {
                // Calculate cooling effectiveness based on temperature change over time
                var duration = result.ContextAfter.Timestamp - result.ContextBefore.Timestamp;
                if (duration.TotalSeconds > 0)
                {
                    var fanSpeed = result.ContextBefore.ThermalState.Fan1Speed * 100 / 255; // Convert to percentage
                    var coolingEffectiveness = _adaptiveFanController.CalculateCoolingEffectiveness(
                        tempBefore,
                        tempAfter,
                        fanSpeed,
                        duration
                    );

                    // Record this data point for learning
                    _adaptiveFanController.RecordThermalPerformance(
                        tempBefore,
                        fanSpeed,
                        coolingEffectiveness
                    );

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Adaptive learning: Recorded thermal performance - Temp: {tempBefore}°C, Fan: {fanSpeed}%, Effectiveness: {coolingEffectiveness}%");
                }
            }

            // Store ML training data for model improvement
            if (_persistenceService != null)
            {
                var duration = result.ContextAfter.Timestamp - result.ContextBefore.Timestamp;
                var trainingData = new ThermalTrainingDataPoint
                {
                    Timestamp = DateTime.UtcNow,
                    TempBefore = result.ContextBefore.ThermalState.CpuTemp,
                    TempAfter = result.ContextAfter.ThermalState.CpuTemp,
                    FanSpeedBefore = (byte)result.ContextBefore.ThermalState.Fan1Speed,
                    FanSpeedAfter = (byte)result.ContextAfter.ThermalState.Fan1Speed,
                    Workload = result.ContextBefore.CurrentWorkload.Type,
                    PowerLevel = result.ContextBefore.PowerState.CurrentPL2,
                    CoolingEffectiveness = _adaptiveFanController != null
                        ? _adaptiveFanController.CalculateCoolingEffectiveness(
                            result.ContextBefore.ThermalState.CpuTemp,
                            result.ContextAfter.ThermalState.CpuTemp,
                            result.ContextBefore.ThermalState.Fan1Speed * 100 / 255,
                            duration)
                        : 0,
                    DurationSeconds = duration.TotalSeconds
                };

                await _persistenceService.StoreThermalTrainingDataAsync(trainingData).ConfigureAwait(false);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"ML training data stored: {trainingData}");
            }
        }
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

    /// <summary>
    /// ELITE SILENT MODE: Media Playback Thermal Management
    /// Target: Whisper-quiet operation (<30 dBA) while maintaining safe temperatures
    /// Strategy: Passive cooling priority, delayed active cooling, capped fan speeds
    /// </summary>
    private void HandleMediaPlaybackThermals(AgentProposal proposal, SystemContext context)
    {
        // SILENT FAN PROFILE
        // Cap fans at 30% (~1650 RPM) for acoustic comfort during movies
        // Media playback with reduced CPU power (20W) generates minimal heat
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "FAN_PROFILE",
            Value = FanProfile.Quiet,
            Reason = "Media playback: Silent mode for acoustic comfort",
            Context = context
        });

        // CAP CPU FAN SPEED (max 30% = ~1650 RPM, whisper quiet)
        var maxMediaFanSpeed = (byte)77; // 77/255 = 30% = ~1650 RPM
        var cpuFanSpeed = Math.Min(maxMediaFanSpeed, (byte)context.ThermalState.Fan1Speed);

        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "FAN_SPEED_CPU",
            Value = cpuFanSpeed,
            Reason = "Media playback: Cap CPU fan for silence (max 30% = 1650 RPM)",
            Context = context
        });

        // CAP GPU FAN SPEED (max 30%)
        var gpuFanSpeed = Math.Min(maxMediaFanSpeed, (byte)context.ThermalState.Fan2Speed);

        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "FAN_SPEED_GPU",
            Value = gpuFanSpeed,
            Reason = "Media playback: Cap GPU fan for silence (max 30% = 1650 RPM)",
            Context = context
        });

        // SAFETY CHECK: If temps rise above 75°C, allow modest fan increase (but still cap at 50%)
        // This prevents thermal issues while maintaining low noise
        if (context.ThermalState.CpuTemp > 75 || context.ThermalState.GpuTemp > 70)
        {
            var safetyFanSpeed = (byte)128; // 50% = ~2750 RPM (still quiet but more cooling)

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "FAN_SPEED_CPU",
                Value = safetyFanSpeed,
                Reason = $"Media playback safety: Temp elevated (CPU:{context.ThermalState.CpuTemp}°C), allow 50% fan",
                Context = context
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Media playback: Safety override - temps elevated, allowing 50% fan speed");
        }

        // CRITICAL SAFETY: If temps reach 85°C, exit silent mode temporarily
        if (context.ThermalState.CpuTemp >= 85 || context.ThermalState.GpuTemp >= 80)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Critical,
                Target = "FAN_PROFILE",
                Value = FanProfile.Balanced,
                Reason = $"CRITICAL: Thermal safety override (CPU:{context.ThermalState.CpuTemp}°C GPU:{context.ThermalState.GpuTemp}°C)",
                Context = context
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Media playback: CRITICAL OVERRIDE - exiting silent mode for thermal safety");
        }

        // REDUCE POLLING FREQUENCY
        // Media playback is thermally stable - reduce from 1Hz to 0.5Hz to save CPU cycles
        proposal.Metadata["ReducedPolling"] = true;
        proposal.Metadata["PollingInterval"] = 2000; // 2 seconds (0.5 Hz)
    }

    /// <summary>
    /// WORK MODE (PRODUCTIVITY): Silent thermal management for office/professional workflows
    /// Target: <25dB fan noise (whisper quiet) while maintaining safe temperatures
    /// Strategy: Passive cooling priority, delayed active cooling, minimal fan speeds
    /// Accept higher temps (80°C CPU, 75°C GPU) for acoustic comfort
    /// </summary>
    private void HandleWorkModeThermals(AgentProposal proposal, SystemContext context)
    {
        // SILENT FAN PROFILE
        // Target <25dB: Off until 60°C, 15% at 70°C, 30% at 80°C
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "FAN_PROFILE",
            Value = FanProfile.Quiet,
            Reason = "Work Mode: Silent operation for office/professional workflows",
            Context = context
        });

        // PASSIVE COOLING PRIORITY
        // Off until 60°C CPU temp (passive cooling sufficient for office work)
        if (context.ThermalState.CpuTemp < 60)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "FAN_SPEED_CPU",
                Value = (byte)0, // Fans off - passive cooling
                Reason = "Work Mode: Passive cooling (<60°C, fans off for silence)",
                Context = context
            });

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "FAN_SPEED_GPU",
                Value = (byte)0, // Fans off
                Reason = "Work Mode: Passive cooling (<60°C, fans off for silence)",
                Context = context
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Work Mode: Passive cooling - fans off (CPU:{context.ThermalState.CpuTemp}°C GPU:{context.ThermalState.GpuTemp}°C)");

            return;
        }

        // 60-70°C: Minimal fan speed (15% = ~825 RPM, barely audible <20dB)
        if (context.ThermalState.CpuTemp < 70)
        {
            var minimalSpeed = (byte)38; // 38/255 = 15% = ~825 RPM

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "FAN_SPEED_CPU",
                Value = minimalSpeed,
                Reason = $"Work Mode: Minimal cooling (CPU:{context.ThermalState.CpuTemp}°C, 15% fan = <20dB)",
                Context = context
            });

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "FAN_SPEED_GPU",
                Value = minimalSpeed,
                Reason = $"Work Mode: Minimal cooling (GPU:{context.ThermalState.GpuTemp}°C, 15% fan = <20dB)",
                Context = context
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Work Mode: Minimal cooling - 15% fan speed (<20dB)");

            return;
        }

        // 70-80°C: Low fan speed (30% = ~1650 RPM, <25dB target)
        if (context.ThermalState.CpuTemp < 80)
        {
            var lowSpeed = (byte)77; // 77/255 = 30% = ~1650 RPM

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "FAN_SPEED_CPU",
                Value = lowSpeed,
                Reason = $"Work Mode: Low cooling (CPU:{context.ThermalState.CpuTemp}°C, 30% fan = <25dB)",
                Context = context
            });

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "FAN_SPEED_GPU",
                Value = lowSpeed,
                Reason = $"Work Mode: Low cooling (GPU:{context.ThermalState.GpuTemp}°C, 30% fan = <25dB)",
                Context = context
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Work Mode: Low cooling - 30% fan speed (<25dB)");

            return;
        }

        // 80-85°C: Moderate fan speed (50% = ~2750 RPM, acceptable noise for thermal safety)
        if (context.ThermalState.CpuTemp < 85)
        {
            var moderateSpeed = (byte)128; // 50% = ~2750 RPM

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "FAN_SPEED_CPU",
                Value = moderateSpeed,
                Reason = $"Work Mode: Moderate cooling (CPU:{context.ThermalState.CpuTemp}°C, 50% fan for thermal safety)",
                Context = context
            });

            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "FAN_SPEED_GPU",
                Value = moderateSpeed,
                Reason = $"Work Mode: Moderate cooling (GPU:{context.ThermalState.GpuTemp}°C, 50% fan for thermal safety)",
                Context = context
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Work Mode: Moderate cooling - 50% fan speed (thermal safety)");

            return;
        }

        // 85°C+: CRITICAL - Exit Work Mode thermal profile, use balanced cooling
        if (context.ThermalState.CpuTemp >= 85 || context.ThermalState.GpuTemp >= 80)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Critical,
                Target = "FAN_PROFILE",
                Value = FanProfile.Balanced,
                Reason = $"CRITICAL: Work Mode override (CPU:{context.ThermalState.CpuTemp}°C GPU:{context.ThermalState.GpuTemp}°C) - thermal safety priority",
                Context = context
            });

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Work Mode: CRITICAL OVERRIDE - exiting silent mode for thermal safety");

            return;
        }

        // REDUCE POLLING FREQUENCY
        // Office work is thermally stable - reduce from 2Hz to 1Hz to save CPU cycles
        proposal.Metadata["ReducedPolling"] = true;
        proposal.Metadata["PollingInterval"] = 1000; // 1 second (1 Hz)
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
