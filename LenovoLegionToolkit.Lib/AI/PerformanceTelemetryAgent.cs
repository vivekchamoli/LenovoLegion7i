using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Performance Telemetry Agent - Autonomous performance monitoring and regression detection
/// Tracks optimization cycle timing, agent proposal latencies, and system responsiveness
///
/// Purpose: Enable proactive performance monitoring and self-healing capability
/// </summary>
public class PerformanceTelemetryAgent
{
    private readonly CognitiveMemoryLayer _cognitiveMemory;
    private readonly Stopwatch _cycleStopwatch = new();
    private readonly Stopwatch _phaseStopwatch = new();

    private const double CycleTimeTarget = 1000.0; // 1 second target
    private const double CycleTimeWarning = 1500.0; // 1.5 second warning threshold
    private const double CycleTimeCritical = 2500.0; // 2.5 second critical threshold

    public PerformanceTelemetryAgent(CognitiveMemoryLayer cognitiveMemory)
    {
        _cognitiveMemory = cognitiveMemory ?? throw new ArgumentNullException(nameof(cognitiveMemory));
    }

    #region Cycle Timing

    /// <summary>
    /// Starts timing an optimization cycle
    /// </summary>
    public void StartOptimizationCycle()
    {
        _cycleStopwatch.Restart();
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[PerformanceTelemetry] Optimization cycle started");
    }

    /// <summary>
    /// Ends timing an optimization cycle and records results
    /// </summary>
    public void EndOptimizationCycle()
    {
        _cycleStopwatch.Stop();
        var elapsedMs = _cycleStopwatch.Elapsed.TotalMilliseconds;

        // Record to cognitive memory
        _cognitiveMemory.UpdatePerformanceMetric("OptimizationCycleTime", elapsedMs, DateTime.Now);

        // Check for anomalies
        var isAnomaly = _cognitiveMemory.DetectAnomaly("OptimizationCycleTime", elapsedMs);

        // Log performance status
        if (elapsedMs > CycleTimeCritical)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[PerformanceTelemetry] ⚠️ CRITICAL: Optimization cycle took {elapsedMs:F0}ms (target: {CycleTimeTarget:F0}ms, threshold: {CycleTimeCritical:F0}ms)");
        }
        else if (elapsedMs > CycleTimeWarning)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[PerformanceTelemetry] ⚠️ WARNING: Optimization cycle took {elapsedMs:F0}ms (target: {CycleTimeTarget:F0}ms, threshold: {CycleTimeWarning:F0}ms)");
        }
        else if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"[PerformanceTelemetry] ✅ Optimization cycle completed in {elapsedMs:F0}ms (within target)");
        }

        if (isAnomaly && Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"[PerformanceTelemetry] 🔍 Performance anomaly detected in optimization cycle timing");
        }
    }

    #endregion

    #region Phase Timing

    /// <summary>
    /// Starts timing a specific phase (e.g., context gathering, agent proposals, arbitration)
    /// </summary>
    public void StartPhase(string phaseName)
    {
        _phaseStopwatch.Restart();
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[PerformanceTelemetry] Phase started: {phaseName}");
    }

    /// <summary>
    /// Ends timing a specific phase and records results
    /// </summary>
    public void EndPhase(string phaseName)
    {
        _phaseStopwatch.Stop();
        var elapsedMs = _phaseStopwatch.Elapsed.TotalMilliseconds;

        // Record to cognitive memory
        var metricName = $"Phase_{phaseName}";
        _cognitiveMemory.UpdatePerformanceMetric(metricName, elapsedMs, DateTime.Now);

        // Check for anomalies
        var isAnomaly = _cognitiveMemory.DetectAnomaly(metricName, elapsedMs);

        if (Log.Instance.IsTraceEnabled)
        {
            var anomalyIndicator = isAnomaly ? " 🔍 ANOMALY" : "";
            Log.Instance.Trace($"[PerformanceTelemetry] Phase completed: {phaseName} - {elapsedMs:F0}ms{anomalyIndicator}");
        }
    }

    #endregion

    #region Agent Profiling

    /// <summary>
    /// Times an agent's proposal generation
    /// </summary>
    public async Task<T> ProfileAgentProposalAsync<T>(string agentName, Func<Task<T>> proposalFunc)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await proposalFunc().ConfigureAwait(false);
            sw.Stop();

            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            var metricName = $"Agent_{agentName}_ProposalLatency";
            _cognitiveMemory.UpdatePerformanceMetric(metricName, elapsedMs, DateTime.Now);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[PerformanceTelemetry] Agent {agentName} proposal: {elapsedMs:F0}ms");

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[PerformanceTelemetry] Agent {agentName} proposal failed after {sw.Elapsed.TotalMilliseconds:F0}ms", ex);
            throw;
        }
    }

    /// <summary>
    /// Times an agent's proposal generation (synchronous version)
    /// </summary>
    public T ProfileAgentProposal<T>(string agentName, Func<T> proposalFunc)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = proposalFunc();
            sw.Stop();

            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            var metricName = $"Agent_{agentName}_ProposalLatency";
            _cognitiveMemory.UpdatePerformanceMetric(metricName, elapsedMs, DateTime.Now);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[PerformanceTelemetry] Agent {agentName} proposal: {elapsedMs:F0}ms");

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[PerformanceTelemetry] Agent {agentName} proposal failed after {sw.Elapsed.TotalMilliseconds:F0}ms", ex);
            throw;
        }
    }

    #endregion

    #region Action Execution Timing

    /// <summary>
    /// Times an action execution
    /// </summary>
    public async Task ProfileActionExecutionAsync(string actionType, Func<Task> actionFunc)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await actionFunc().ConfigureAwait(false);
            sw.Stop();

            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            var metricName = $"Action_{actionType}_ExecutionTime";
            _cognitiveMemory.UpdatePerformanceMetric(metricName, elapsedMs, DateTime.Now);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[PerformanceTelemetry] Action {actionType} executed: {elapsedMs:F0}ms");
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[PerformanceTelemetry] Action {actionType} failed after {sw.Elapsed.TotalMilliseconds:F0}ms", ex);
            throw;
        }
    }

    #endregion

    #region Diagnostics

    /// <summary>
    /// Gets performance statistics for a specific metric
    /// </summary>
    public PerformanceMetricStats? GetMetricStats(string metricName)
    {
        return _cognitiveMemory.GetMetricStats(metricName);
    }

    /// <summary>
    /// Gets performance statistics for all optimization cycle metrics
    /// </summary>
    public PerformanceSummary GetPerformanceSummary()
    {
        var cycleStats = _cognitiveMemory.GetMetricStats("OptimizationCycleTime");
        var contextStats = _cognitiveMemory.GetMetricStats("Phase_ContextGathering");
        var proposalStats = _cognitiveMemory.GetMetricStats("Phase_AgentProposals");
        var arbitrationStats = _cognitiveMemory.GetMetricStats("Phase_Arbitration");
        var executionStats = _cognitiveMemory.GetMetricStats("Phase_ActionExecution");

        return new PerformanceSummary
        {
            CycleTime = cycleStats,
            ContextGathering = contextStats,
            AgentProposals = proposalStats,
            Arbitration = arbitrationStats,
            ActionExecution = executionStats,
            Timestamp = DateTime.Now
        };
    }

    /// <summary>
    /// Checks if current performance is within acceptable thresholds
    /// </summary>
    public PerformanceHealthStatus GetHealthStatus()
    {
        var cycleStats = _cognitiveMemory.GetMetricStats("OptimizationCycleTime");
        if (cycleStats == null)
            return PerformanceHealthStatus.Unknown;

        if (cycleStats.LastValue > CycleTimeCritical)
            return PerformanceHealthStatus.Critical;

        if (cycleStats.LastValue > CycleTimeWarning)
            return PerformanceHealthStatus.Warning;

        if (cycleStats.Mean > CycleTimeTarget)
            return PerformanceHealthStatus.Degraded;

        return PerformanceHealthStatus.Healthy;
    }

    /// <summary>
    /// Gets a human-readable performance report
    /// </summary>
    public string GetPerformanceReport()
    {
        var summary = GetPerformanceSummary();
        var health = GetHealthStatus();
        var memoryStatus = _cognitiveMemory.GetStatus();

        return $@"
=== Performance Telemetry Report ===
Timestamp: {summary.Timestamp:yyyy-MM-dd HH:mm:ss}
Health Status: {health}

Optimization Cycle Time:
  Current: {summary.CycleTime?.LastValue:F0}ms
  Average: {summary.CycleTime?.Mean:F0}ms ± {summary.CycleTime?.StdDev:F0}ms
  Range: [{summary.CycleTime?.Min:F0}ms - {summary.CycleTime?.Max:F0}ms]
  Samples: {summary.CycleTime?.Count ?? 0}

Phase Breakdown:
  Context Gathering: {summary.ContextGathering?.LastValue:F0}ms (avg: {summary.ContextGathering?.Mean:F0}ms)
  Agent Proposals: {summary.AgentProposals?.LastValue:F0}ms (avg: {summary.AgentProposals?.Mean:F0}ms)
  Arbitration: {summary.Arbitration?.LastValue:F0}ms (avg: {summary.Arbitration?.Mean:F0}ms)
  Action Execution: {summary.ActionExecution?.LastValue:F0}ms (avg: {summary.ActionExecution?.Mean:F0}ms)

Cognitive Memory Status:
  Workload Patterns: {memoryStatus.WorkloadPatternCount}
  Optimization History: {memoryStatus.OptimizationHistoryCount}
  Performance Metrics: {memoryStatus.PerformanceMetricCount}
  Memory Usage: {memoryStatus.MemoryUsageMB:F2}MB

Target Compliance:
  Target: {CycleTimeTarget:F0}ms
  Warning: {CycleTimeWarning:F0}ms
  Critical: {CycleTimeCritical:F0}ms
  Status: {(summary.CycleTime?.LastValue <= CycleTimeTarget ? "✅ WITHIN TARGET" : summary.CycleTime?.LastValue <= CycleTimeWarning ? "⚠️ ABOVE TARGET" : "❌ CRITICAL")}
";
    }

    #endregion

    #region Self-Healing

    /// <summary>
    /// Detects performance regressions and suggests corrective actions
    /// </summary>
    public PerformanceAnalysisResult AnalyzePerformance()
    {
        var result = new PerformanceAnalysisResult();
        var cycleStats = _cognitiveMemory.GetMetricStats("OptimizationCycleTime");

        if (cycleStats == null)
        {
            result.Status = PerformanceHealthStatus.Unknown;
            result.Recommendations.Add("Insufficient data for performance analysis");
            return result;
        }

        result.Status = GetHealthStatus();
        result.CurrentCycleTime = cycleStats.LastValue;
        result.AverageCycleTime = cycleStats.Mean;
        result.TargetCycleTime = CycleTimeTarget;

        // Analyze bottlenecks
        var proposalStats = _cognitiveMemory.GetMetricStats("Phase_AgentProposals");
        var contextStats = _cognitiveMemory.GetMetricStats("Phase_ContextGathering");
        var executionStats = _cognitiveMemory.GetMetricStats("Phase_ActionExecution");

        if (proposalStats != null && proposalStats.Mean > 1000)
        {
            result.Bottlenecks.Add($"Agent proposals: {proposalStats.Mean:F0}ms (high latency)");
            result.Recommendations.Add("Consider parallelizing agent proposal generation");
        }

        if (contextStats != null && contextStats.Mean > 500)
        {
            result.Bottlenecks.Add($"Context gathering: {contextStats.Mean:F0}ms (high latency)");
            result.Recommendations.Add("Optimize system context retrieval operations");
        }

        if (executionStats != null && executionStats.Mean > 500)
        {
            result.Bottlenecks.Add($"Action execution: {executionStats.Mean:F0}ms (high latency)");
            result.Recommendations.Add("Verify idempotency checks are working correctly");
        }

        // Check for trends
        if (cycleStats.Mean > cycleStats.LastValue * 1.5)
        {
            result.Recommendations.Add("Performance improving: recent cycles faster than average");
        }
        else if (cycleStats.LastValue > cycleStats.Mean * 1.5)
        {
            result.Recommendations.Add("Performance degrading: recent cycle significantly slower than average");
        }

        return result;
    }

    #endregion
}

#region Data Models

/// <summary>
/// Summary of performance metrics across all phases
/// </summary>
public record PerformanceSummary
{
    public PerformanceMetricStats? CycleTime { get; init; }
    public PerformanceMetricStats? ContextGathering { get; init; }
    public PerformanceMetricStats? AgentProposals { get; init; }
    public PerformanceMetricStats? Arbitration { get; init; }
    public PerformanceMetricStats? ActionExecution { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Performance health status levels
/// </summary>
public enum PerformanceHealthStatus
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Warning = 3,
    Critical = 4
}

/// <summary>
/// Results of performance analysis with recommendations
/// </summary>
public class PerformanceAnalysisResult
{
    public PerformanceHealthStatus Status { get; set; } = PerformanceHealthStatus.Unknown;
    public double CurrentCycleTime { get; set; }
    public double AverageCycleTime { get; set; }
    public double TargetCycleTime { get; set; }
    public List<string> Bottlenecks { get; init; } = new();
    public List<string> Recommendations { get; init; } = new();
}

#endregion
