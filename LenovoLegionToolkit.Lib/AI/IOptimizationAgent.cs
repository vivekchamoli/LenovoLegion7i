using System.Collections.Generic;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Base interface for all optimization agents in the multi-agentic system
/// Each agent analyzes system context and proposes resource actions
/// </summary>
public interface IOptimizationAgent
{
    /// <summary>
    /// Agent name for logging and conflict resolution
    /// </summary>
    string AgentName { get; }

    /// <summary>
    /// Agent priority level for conflict resolution
    /// </summary>
    AgentPriority Priority { get; }

    /// <summary>
    /// Analyze current system context and propose optimization actions
    /// </summary>
    Task<AgentProposal> ProposeActionsAsync(SystemContext context);

    /// <summary>
    /// Called after actions are executed - allows agent to learn from outcomes
    /// </summary>
    Task OnActionsExecutedAsync(ExecutionResult result);
}

/// <summary>
/// Agent priority levels for conflict resolution
/// </summary>
public enum AgentPriority
{
    Critical = 100,  // Thermal emergencies, battery critical
    High = 80,       // Power management, thermal proactive
    Medium = 50,     // GPU optimization, workload adaptation
    Low = 30,        // Background optimizations, telemetry
    Opportunistic = 10 // Nice-to-have optimizations
}

/// <summary>
/// Proposal from an agent containing requested actions
/// </summary>
public class AgentProposal
{
    public string Agent { get; set; } = string.Empty;
    public AgentPriority Priority { get; set; }
    public List<ResourceAction> Actions { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Resource action type classification
/// </summary>
public enum ActionType
{
    Emergency,      // Immediate execution required (thermal throttling prevention)
    Critical,       // High priority (battery critical, performance degradation)
    Proactive,      // Preventive action (increase cooling before temps rise)
    Reactive,       // Response to current state
    Opportunistic   // Performance enhancement when conditions allow
}

/// <summary>
/// Single resource action to be executed
/// </summary>
public class ResourceAction
{
    public ActionType Type { get; set; }
    public string Target { get; set; } = string.Empty;
    public object Value { get; set; } = null!;
    public string Reason { get; set; } = string.Empty;
    public List<int>? AffectedProcesses { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// System context at the time of action creation
    /// Used by handlers for context-aware execution (e.g., acoustic optimization)
    /// </summary>
    public SystemContext? Context { get; set; }
}

/// <summary>
/// Result of action execution for agent learning
/// </summary>
public class ExecutionResult
{
    public bool Success { get; set; }
    public List<ResourceAction> ExecutedActions { get; set; } = new();
    public List<Conflict> ResolvedConflicts { get; set; } = new();
    public SystemContext ContextBefore { get; set; } = null!;
    public SystemContext ContextAfter { get; set; } = null!;
    public Dictionary<string, object> Metrics { get; set; } = new();
}

/// <summary>
/// Conflict between competing agent proposals
/// </summary>
public class Conflict
{
    public string Target { get; set; } = string.Empty;
    public ResourceAction Winner { get; set; } = null!;
    public List<ResourceAction> Losers { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}
