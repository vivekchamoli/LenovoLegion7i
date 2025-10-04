using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Agent Coordinator - Advanced multi-agent coordination and collaboration
/// Enables agents to share information and coordinate complex actions
/// </summary>
public class AgentCoordinator
{
    private readonly Dictionary<string, AgentState> _agentStates = new();
    private readonly List<CoordinationSignal> _activeSignals = new();
    private readonly object _lock = new();

    /// <summary>
    /// Broadcast a coordination signal to all agents
    /// </summary>
    public void BroadcastSignal(CoordinationSignal signal)
    {
        lock (_lock)
        {
            _activeSignals.Add(signal);

            // Remove expired signals
            _activeSignals.RemoveAll(s => (DateTime.Now - s.Timestamp).TotalMinutes > 5);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Coordination signal broadcast: {signal.Type} from {signal.SourceAgent}");
        }
    }

    /// <summary>
    /// Get active coordination signals for an agent
    /// </summary>
    public List<CoordinationSignal> GetActiveSignals(string agentName)
    {
        lock (_lock)
        {
            return _activeSignals
                .Where(s => s.TargetAgents == null || s.TargetAgents.Contains(agentName))
                .Where(s => s.SourceAgent != agentName)
                .ToList();
        }
    }

    /// <summary>
    /// Update agent state for coordination
    /// </summary>
    public void UpdateAgentState(string agentName, AgentState state)
    {
        lock (_lock)
        {
            _agentStates[agentName] = state;
            state.LastUpdate = DateTime.Now;
        }
    }

    /// <summary>
    /// Get state of another agent
    /// </summary>
    public AgentState? GetAgentState(string agentName)
    {
        lock (_lock)
        {
            return _agentStates.TryGetValue(agentName, out var state) ? state : null;
        }
    }

    /// <summary>
    /// Request coordination from other agents
    /// </summary>
    public async Task<List<ResourceAction>> RequestCoordinatedActionsAsync(
        string requestingAgent,
        CoordinationType type,
        SystemContext context,
        IEnumerable<IOptimizationAgent> agents)
    {
        var coordinatedActions = new List<ResourceAction>();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"{requestingAgent} requesting {type} coordination");

        // Broadcast signal
        BroadcastSignal(new CoordinationSignal
        {
            Type = type,
            SourceAgent = requestingAgent,
            Timestamp = DateTime.Now,
            Context = context
        });

        // Collect proposals from other agents
        foreach (var agent in agents)
        {
            if (agent.AgentName == requestingAgent)
                continue;

            try
            {
                var proposal = await agent.ProposeActionsAsync(context).ConfigureAwait(false);

                // Filter actions that match coordination type
                var relevantActions = proposal.Actions
                    .Where(a => IsRelevantForCoordination(a, type))
                    .ToList();

                coordinatedActions.AddRange(relevantActions);
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Failed to get coordinated proposal from {agent.AgentName}", ex);
            }
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Collected {coordinatedActions.Count} coordinated actions");

        return coordinatedActions;
    }

    /// <summary>
    /// Check if emergency mode is active (multiple agents requesting emergency actions)
    /// </summary>
    public bool IsEmergencyMode()
    {
        lock (_lock)
        {
            var emergencySignals = _activeSignals
                .Where(s => s.Type == CoordinationType.Emergency)
                .Where(s => (DateTime.Now - s.Timestamp).TotalMinutes < 2)
                .Count();

            return emergencySignals >= 2; // At least 2 agents signaling emergency
        }
    }

    /// <summary>
    /// Get current coordination mode based on active signals
    /// </summary>
    public CoordinationMode GetCurrentMode()
    {
        lock (_lock)
        {
            var recentSignals = _activeSignals
                .Where(s => (DateTime.Now - s.Timestamp).TotalMinutes < 2)
                .ToList();

            if (recentSignals.Any(s => s.Type == CoordinationType.Emergency))
                return CoordinationMode.Emergency;

            if (recentSignals.Any(s => s.Type == CoordinationType.BatteryCritical))
                return CoordinationMode.BatterySaving;

            if (recentSignals.Any(s => s.Type == CoordinationType.HighPowerConsumption))
                return CoordinationMode.PowerOptimization;

            if (recentSignals.Any(s => s.Type == CoordinationType.ThermalThrottling))
                return CoordinationMode.ThermalManagement;

            return CoordinationMode.Normal;
        }
    }

    /// <summary>
    /// Calculate system-wide optimization priority
    /// </summary>
    public OptimizationPriority CalculateGlobalPriority(SystemContext context)
    {
        var mode = GetCurrentMode();

        // Emergency mode: survival first
        if (mode == CoordinationMode.Emergency)
        {
            return new OptimizationPriority
            {
                BatteryConservation = 1.0,
                Performance = 0.1,
                ThermalManagement = 0.5,
                UserExperience = 0.3
            };
        }

        // Battery saving: extend runtime
        if (mode == CoordinationMode.BatterySaving || context.BatteryState.ChargePercent < 20)
        {
            return new OptimizationPriority
            {
                BatteryConservation = 0.9,
                Performance = 0.2,
                ThermalManagement = 0.4,
                UserExperience = 0.5
            };
        }

        // Thermal management: prevent throttling
        if (mode == CoordinationMode.ThermalManagement)
        {
            return new OptimizationPriority
            {
                BatteryConservation = 0.5,
                Performance = 0.6,
                ThermalManagement = 1.0,
                UserExperience = 0.7
            };
        }

        // Power optimization: balance all factors
        if (mode == CoordinationMode.PowerOptimization)
        {
            return new OptimizationPriority
            {
                BatteryConservation = 0.7,
                Performance = 0.6,
                ThermalManagement = 0.6,
                UserExperience = 0.8
            };
        }

        // Normal: user experience first
        return new OptimizationPriority
        {
            BatteryConservation = 0.4,
            Performance = 0.8,
            ThermalManagement = 0.5,
            UserExperience = 1.0
        };
    }

    private bool IsRelevantForCoordination(ResourceAction action, CoordinationType type)
    {
        return type switch
        {
            CoordinationType.Emergency => action.Type == ActionType.Critical || action.Type == ActionType.Emergency,
            CoordinationType.BatteryCritical => action.Target.Contains("BATTERY") || action.Target.Contains("DISPLAY") || action.Target.Contains("GPU"),
            CoordinationType.ThermalThrottling => action.Target.Contains("CPU") || action.Target.Contains("GPU") || action.Target.Contains("FAN"),
            CoordinationType.HighPowerConsumption => action.Target.Contains("POWER") || action.Target.Contains("GPU") || action.Target.Contains("CPU"),
            _ => false
        };
    }
}

/// <summary>
/// Coordination signal between agents
/// </summary>
public class CoordinationSignal
{
    public CoordinationType Type { get; set; }
    public string SourceAgent { get; set; } = string.Empty;
    public List<string>? TargetAgents { get; set; }
    public DateTime Timestamp { get; set; }
    public SystemContext? Context { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// Agent state for coordination
/// </summary>
public class AgentState
{
    public string AgentName { get; set; } = string.Empty;
    public DateTime LastUpdate { get; set; }
    public int ActionsProposed { get; set; }
    public int ActionsExecuted { get; set; }
    public double SuccessRate { get; set; }
    public Dictionary<string, object> StateData { get; set; } = new();
}

/// <summary>
/// Types of coordination
/// </summary>
public enum CoordinationType
{
    Emergency,              // Critical situation requiring immediate action
    BatteryCritical,        // Battery extremely low
    ThermalThrottling,      // System overheating
    HighPowerConsumption,   // Excessive power draw
    UserOverride,           // User manually changed something
    WorkloadChange,         // Workload type changed
    Normal                  // Regular coordination
}

/// <summary>
/// Coordination mode
/// </summary>
public enum CoordinationMode
{
    Normal,
    Emergency,
    BatterySaving,
    ThermalManagement,
    PowerOptimization
}

/// <summary>
/// System-wide optimization priorities
/// </summary>
public class OptimizationPriority
{
    public double BatteryConservation { get; set; }  // 0.0 to 1.0
    public double Performance { get; set; }           // 0.0 to 1.0
    public double ThermalManagement { get; set; }     // 0.0 to 1.0
    public double UserExperience { get; set; }        // 0.0 to 1.0
}
