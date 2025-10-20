using System;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.AI.Elite;

/// <summary>
/// Base interface for all elite sub-agents
/// </summary>
public interface IEliteSubAgent : IDisposable
{
    /// <summary>
    /// Unique agent identifier
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Agent type classification
    /// </summary>
    SubAgentType Type { get; }

    /// <summary>
    /// Agent priority (0-10, higher = more important)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Start the sub-agent
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Execute one orchestration cycle
    /// Called at 100Hz by orchestrator
    /// </summary>
    Task ExecuteCycleAsync(FusedTelemetry telemetry, CancellationToken ct);

    /// <summary>
    /// Stop the sub-agent gracefully
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Get agent health status
    /// </summary>
    AgentHealth GetHealth();
}

/// <summary>
/// Base implementation for sub-agents
/// </summary>
public abstract class EliteSubAgentBase : IEliteSubAgent
{
    protected readonly SecureAgentBus _agentBus;
    protected readonly TelemetryFusionEngine _telemetryEngine;

    protected bool _isRunning;
    protected long _totalCycles;
    protected long _totalErrors;
    protected DateTime _startTime;

    public string AgentId { get; }
    public abstract SubAgentType Type { get; }
    public virtual int Priority => 5; // Default medium priority

    protected EliteSubAgentBase(
        string agentId,
        SecureAgentBus agentBus,
        TelemetryFusionEngine telemetryEngine)
    {
        AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        _agentBus = agentBus ?? throw new ArgumentNullException(nameof(agentBus));
        _telemetryEngine = telemetryEngine ?? throw new ArgumentNullException(nameof(telemetryEngine));
    }

    public virtual Task StartAsync()
    {
        _isRunning = true;
        _startTime = DateTime.UtcNow;

        // Register with message bus
        _agentBus.RegisterAgent(AgentId);
        _agentBus.SubscribeToBroadcasts(AgentId);

        return Task.CompletedTask;
    }

    public abstract Task ExecuteCycleAsync(FusedTelemetry telemetry, CancellationToken ct);

    public virtual Task StopAsync()
    {
        _isRunning = false;

        // Unregister from message bus
        _agentBus.UnregisterAgent(AgentId);

        return Task.CompletedTask;
    }

    public virtual AgentHealth GetHealth()
    {
        var uptime = DateTime.UtcNow - _startTime;
        var errorRate = _totalCycles > 0 ? (double)_totalErrors / _totalCycles : 0;

        return new AgentHealth
        {
            AgentId = AgentId,
            IsHealthy = _isRunning && errorRate < 0.05, // <5% error rate
            Uptime = uptime,
            TotalCycles = _totalCycles,
            TotalErrors = _totalErrors,
            ErrorRate = errorRate,
            Status = _isRunning ? AgentStatus.Running : AgentStatus.Stopped
        };
    }

    public virtual void Dispose()
    {
        _isRunning = false;
        _agentBus.UnregisterAgent(AgentId);
    }
}

/// <summary>
/// Agent health status
/// </summary>
public class AgentHealth
{
    public string AgentId { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public TimeSpan Uptime { get; set; }
    public long TotalCycles { get; set; }
    public long TotalErrors { get; set; }
    public double ErrorRate { get; set; }
    public AgentStatus Status { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Agent status enum
/// </summary>
public enum AgentStatus
{
    Initializing,
    Running,
    Paused,
    Stopped,
    Error,
    ShuttingDown
}
