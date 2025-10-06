using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.AI;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Multi-Step Planner - Predicts and prevents agent conflicts
/// Phase 3: Elite optimization - looks 3-5 steps ahead to avoid conflicts
/// </summary>
public class MultiStepPlanner
{
    private readonly Dictionary<string, AgentActionHistory> _agentHistory = new();
    private readonly object _lockObj = new();

    /// <summary>
    /// Analyze proposal for potential conflicts with other agents
    /// Looks ahead 3-5 steps to predict cascading effects
    /// </summary>
    public ConflictAnalysis AnalyzeProposal(AgentProposal proposal, SystemContext context)
    {
        var analysis = new ConflictAnalysis
        {
            AgentName = proposal.Agent,
            HasConflict = false,
            ConflictingAgents = new List<string>(),
            PredictedSteps = new List<PredictedStep>()
        };

        if (proposal.Actions.Count == 0)
            return analysis;

        lock (_lockObj)
        {
            // Step 1: Check immediate conflicts
            foreach (var action in proposal.Actions)
            {
                var immediateConflict = CheckImmediateConflict(action, proposal.Agent, context);
                if (immediateConflict != null)
                {
                    analysis.HasConflict = true;
                    analysis.ConflictingAgents.Add(immediateConflict);
                    analysis.ConflictReason = $"Immediate conflict with {immediateConflict}";
                }
            }

            // Step 2: Predict cascading effects (3-5 steps ahead)
            var predictedSteps = PredictCascadingEffects(proposal, context, maxSteps: 5);
            analysis.PredictedSteps = predictedSteps;

            // Step 3: Check for oscillation patterns
            var oscillationRisk = DetectOscillationRisk(proposal.Agent, proposal.Actions);
            if (oscillationRisk != null)
            {
                analysis.HasConflict = true;
                analysis.ConflictReason = oscillationRisk;
            }

            // Step 4: Record action for future conflict detection
            RecordAction(proposal.Agent, proposal.Actions);
        }

        return analysis;
    }

    /// <summary>
    /// Check for immediate conflicts with recently executed actions
    /// </summary>
    private string? CheckImmediateConflict(ResourceAction action, string proposingAgent, SystemContext context)
    {
        // GPU mode conflicts
        if (action.Target == "GPU_HYBRID_MODE")
        {
            // Check if another agent just changed GPU mode (< 30 seconds ago)
            foreach (var kvp in _agentHistory)
            {
                if (kvp.Key == proposingAgent)
                    continue; // Skip self

                var recentGpuAction = kvp.Value.RecentActions
                    .Where(a => a.Target == "GPU_HYBRID_MODE")
                    .OrderByDescending(a => a.Timestamp)
                    .FirstOrDefault();

                if (recentGpuAction != null &&
                    (DateTime.Now - recentGpuAction.Timestamp).TotalSeconds < 30)
                {
                    return kvp.Key; // Conflict with this agent
                }
            }
        }

        // Fan speed conflicts
        if (action.Target == "FAN_SPEED_CPU" || action.Target == "FAN_SPEED_GPU")
        {
            // Check if ThermalAgent just adjusted fans
            if (_agentHistory.TryGetValue("ThermalAgent", out var thermalHistory))
            {
                var recentFanAction = thermalHistory.RecentActions
                    .Where(a => a.Target.StartsWith("FAN_SPEED"))
                    .OrderByDescending(a => a.Timestamp)
                    .FirstOrDefault();

                if (recentFanAction != null &&
                    (DateTime.Now - recentFanAction.Timestamp).TotalSeconds < 15)
                {
                    return "ThermalAgent"; // Let thermal agent win (Critical priority)
                }
            }
        }

        return null; // No immediate conflict
    }

    /// <summary>
    /// Predict cascading effects of an action (3-5 steps ahead)
    /// </summary>
    private List<PredictedStep> PredictCascadingEffects(AgentProposal proposal, SystemContext context, int maxSteps)
    {
        var steps = new List<PredictedStep>();
        var currentContext = context;

        for (int i = 0; i < maxSteps; i++)
        {
            var step = new PredictedStep
            {
                StepNumber = i + 1,
                Predictions = new List<string>()
            };

            foreach (var action in proposal.Actions)
            {
                // Predict GPU mode change effects
                if (action.Target == "GPU_HYBRID_MODE" && action.Value is GPUTransitionProposal gpuProposal)
                {
                    var targetMode = gpuProposal.TargetMode;

                    // Step 1: GPU switches
                    if (i == 0)
                    {
                        step.Predictions.Add($"GPU mode → {targetMode}");
                    }

                    // Step 2: Power consumption changes
                    if (i == 1)
                    {
                        if (targetMode == HybridModeState.OnIGPUOnly)
                        {
                            step.Predictions.Add($"Power consumption: -15W to -30W (dGPU disabled)");
                        }
                        else if (targetMode == HybridModeState.Off)
                        {
                            step.Predictions.Add($"Power consumption: +15W to +30W (dGPU always on)");
                        }
                    }

                    // Step 3: Thermal impact
                    if (i == 2)
                    {
                        if (targetMode == HybridModeState.OnIGPUOnly)
                        {
                            step.Predictions.Add($"GPU temp: -5°C to -10°C (dGPU idle)");
                        }
                        else if (targetMode == HybridModeState.Off)
                        {
                            step.Predictions.Add($"GPU temp: +5°C to +10°C (dGPU active)");
                        }
                    }

                    // Step 4: Fan response
                    if (i == 3)
                    {
                        if (targetMode == HybridModeState.Off &&
                            currentContext.ThermalState.GpuTemp > 70)
                        {
                            step.Predictions.Add($"ThermalAgent likely to increase fan speed (+10-20%)");
                        }
                        else if (targetMode == HybridModeState.OnIGPUOnly)
                        {
                            step.Predictions.Add($"ThermalAgent may reduce fan speed (-10-20%)");
                        }
                    }

                    // Step 5: Battery impact
                    if (i == 4 && currentContext.BatteryState.IsOnBattery)
                    {
                        if (targetMode == HybridModeState.OnIGPUOnly)
                        {
                            step.Predictions.Add($"Battery life: +30min to +1h (power savings)");
                        }
                        else if (targetMode == HybridModeState.Off)
                        {
                            step.Predictions.Add($"Battery life: -30min to -1h (increased consumption)");
                        }
                    }
                }

                // Predict fan speed change effects
                if (action.Target == "FAN_SPEED_CPU" || action.Target == "FAN_SPEED_GPU")
                {
                    // Step 1: Fan speed changes
                    if (i == 0)
                    {
                        step.Predictions.Add($"{action.Target} → {action.Value}%");
                    }

                    // Step 2: Acoustic impact
                    if (i == 1)
                    {
                        var speed = Convert.ToInt32(action.Value);
                        if (speed > 60)
                        {
                            step.Predictions.Add($"Noise level: Noticeable (> 35 dBA)");
                        }
                        else if (speed < 30)
                        {
                            step.Predictions.Add($"Noise level: Silent (< 25 dBA)");
                        }
                    }

                    // Step 3: Thermal response
                    if (i == 2)
                    {
                        var speed = Convert.ToInt32(action.Value);
                        if (speed > 50)
                        {
                            step.Predictions.Add($"Temperature: -3°C to -8°C (better cooling)");
                        }
                    }
                }
            }

            if (step.Predictions.Count > 0)
                steps.Add(step);
        }

        return steps;
    }

    /// <summary>
    /// Detect oscillation risk (agents fighting each other)
    /// </summary>
    private string? DetectOscillationRisk(string agentName, List<ResourceAction> actions)
    {
        if (!_agentHistory.TryGetValue(agentName, out var history))
            return null;

        foreach (var action in actions)
        {
            // Check for rapid back-and-forth changes (< 2 minutes)
            var recentSameTarget = history.RecentActions
                .Where(a => a.Target == action.Target)
                .OrderByDescending(a => a.Timestamp)
                .Take(3)
                .ToList();

            if (recentSameTarget.Count >= 3)
            {
                var allRecent = recentSameTarget.All(a =>
                    (DateTime.Now - a.Timestamp).TotalMinutes < 2);

                if (allRecent)
                {
                    return $"Oscillation risk detected: {action.Target} changed 3+ times in 2 minutes";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Record executed action for conflict detection
    /// </summary>
    private void RecordAction(string agentName, List<ResourceAction> actions)
    {
        if (!_agentHistory.ContainsKey(agentName))
        {
            _agentHistory[agentName] = new AgentActionHistory
            {
                AgentName = agentName,
                RecentActions = new List<RecordedAction>()
            };
        }

        var history = _agentHistory[agentName];

        foreach (var action in actions)
        {
            history.RecentActions.Add(new RecordedAction
            {
                Target = action.Target,
                Value = action.Value,
                Timestamp = DateTime.Now
            });
        }

        // Keep only last 50 actions per agent
        if (history.RecentActions.Count > 50)
        {
            history.RecentActions = history.RecentActions
                .OrderByDescending(a => a.Timestamp)
                .Take(50)
                .ToList();
        }
    }

    /// <summary>
    /// Get coordination statistics
    /// </summary>
    public CoordinationStats GetStatistics()
    {
        lock (_lockObj)
        {
            return new CoordinationStats
            {
                TrackedAgents = _agentHistory.Count,
                TotalActionsRecorded = _agentHistory.Sum(kvp => kvp.Value.RecentActions.Count)
            };
        }
    }
}

/// <summary>
/// Conflict Analysis Result
/// </summary>
public class ConflictAnalysis
{
    public string AgentName { get; set; } = string.Empty;
    public bool HasConflict { get; set; }
    public List<string> ConflictingAgents { get; set; } = new();
    public string? ConflictReason { get; set; }
    public List<PredictedStep> PredictedSteps { get; set; } = new();
}

/// <summary>
/// Predicted Step in Multi-Step Analysis
/// </summary>
public class PredictedStep
{
    public int StepNumber { get; set; }
    public List<string> Predictions { get; set; } = new();
}

/// <summary>
/// Agent Action History
/// </summary>
public class AgentActionHistory
{
    public string AgentName { get; set; } = string.Empty;
    public List<RecordedAction> RecentActions { get; set; } = new();
}

/// <summary>
/// Recorded Action
/// </summary>
public class RecordedAction
{
    public string Target { get; set; } = string.Empty;
    public object? Value { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Coordination Statistics
/// </summary>
public class CoordinationStats
{
    public int TrackedAgents { get; set; }
    public int TotalActionsRecorded { get; set; }
}
