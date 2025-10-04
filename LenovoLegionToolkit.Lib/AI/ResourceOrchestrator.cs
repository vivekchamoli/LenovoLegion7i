using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Utils;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Resource Orchestrator - Central multi-agent coordination system
/// Replaces siloed controller approach with unified intelligence
/// Coordinates Thermal, Power, GPU, and Battery agents for optimal system performance
/// </summary>
public class ResourceOrchestrator : IDisposable
{
    private bool _disposed;
    private readonly SystemContextStore _contextStore;
    private readonly DecisionArbitrationEngine _arbitrator;
    private readonly ActionExecutor _actionExecutor;
    private readonly UserBehaviorAnalyzer? _behaviorAnalyzer;
    private readonly UserPreferenceTracker? _preferenceTracker;
    private readonly AgentCoordinator? _agentCoordinator;
    private readonly List<IOptimizationAgent> _agents = new();
    private readonly Gen9ECController? _gen9EcController;
    private readonly GPUController _gpuController;
    private readonly AsyncLock _orchestrationLock = new();

    private Task? _optimizationLoopTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;

    // Performance metrics
    private long _totalOptimizationCycles;
    private long _totalActionsExecuted;
    private long _totalConflictsResolved;
    private readonly Stopwatch _uptimeStopwatch = new();

    public event EventHandler<OptimizationCycleCompleted>? CycleCompleted;

    public bool IsRunning => _isRunning;
    public long TotalCycles => _totalOptimizationCycles;
    public long TotalActions => _totalActionsExecuted;
    public long TotalConflicts => _totalConflictsResolved;
    public TimeSpan UpTime => _uptimeStopwatch.Elapsed;

    public ResourceOrchestrator(
        SystemContextStore contextStore,
        DecisionArbitrationEngine arbitrator,
        ActionExecutor actionExecutor,
        Gen9ECController? gen9EcController,
        GPUController gpuController,
        UserBehaviorAnalyzer? behaviorAnalyzer = null,
        UserPreferenceTracker? preferenceTracker = null,
        AgentCoordinator? agentCoordinator = null)
    {
        _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
        _arbitrator = arbitrator ?? throw new ArgumentNullException(nameof(arbitrator));
        _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        _gen9EcController = gen9EcController;
        _gpuController = gpuController ?? throw new ArgumentNullException(nameof(gpuController));
        _behaviorAnalyzer = behaviorAnalyzer;
        _preferenceTracker = preferenceTracker;
        _agentCoordinator = agentCoordinator;
    }

    /// <summary>
    /// Register an optimization agent with the orchestrator
    /// </summary>
    public void RegisterAgent(IOptimizationAgent agent)
    {
        if (agent == null)
            throw new ArgumentNullException(nameof(agent));

        _agents.Add(agent);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Registered agent: {agent.AgentName} (Priority: {agent.Priority})");
    }

    /// <summary>
    /// Start the optimization loop
    /// </summary>
    public Task StartAsync(int optimizationIntervalMs = 500)
    {
        if (_isRunning)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Already running");
            return Task.CompletedTask;
        }

        if (_agents.Count == 0)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"No agents registered - cannot start");
            throw new InvalidOperationException("No optimization agents registered");
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting Resource Orchestrator with {_agents.Count} agents [interval={optimizationIntervalMs}ms]");

        _cancellationTokenSource = new CancellationTokenSource();
        _isRunning = true;
        _uptimeStopwatch.Restart();

        _optimizationLoopTask = Task.Run(
            () => OptimizationLoopAsync(optimizationIntervalMs, _cancellationTokenSource.Token),
            _cancellationTokenSource.Token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop the optimization loop
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Stopping Resource Orchestrator...");

        _cancellationTokenSource?.Cancel();

        if (_optimizationLoopTask != null)
        {
            try
            {
                await _optimizationLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _isRunning = false;
        _uptimeStopwatch.Stop();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Stopped. Total cycles: {_totalOptimizationCycles}, Total actions: {_totalActionsExecuted}");
    }

    /// <summary>
    /// Main optimization loop - coordinates all agents
    /// Runs continuously at specified interval
    /// </summary>
    private async Task OptimizationLoopAsync(int intervalMs, CancellationToken ct)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Optimization loop started");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var cycleStopwatch = Stopwatch.StartNew();

                try
                {
                    await ExecuteOptimizationCycleAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Optimization cycle error", ex);
                }

                cycleStopwatch.Stop();

                // Log slow cycles
                if (cycleStopwatch.ElapsedMilliseconds > intervalMs * 1.5)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"WARNING: Slow optimization cycle: {cycleStopwatch.ElapsedMilliseconds}ms (target: {intervalMs}ms)");
                }

                // Wait for next cycle
                var delay = Math.Max(0, intervalMs - (int)cycleStopwatch.ElapsedMilliseconds);
                if (delay > 0)
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Optimization loop cancelled");
        }
        finally
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Optimization loop ended");
        }
    }

    /// <summary>
    /// Execute single optimization cycle
    /// 1. Gather system context
    /// 2. Get proposals from all agents
    /// 3. Arbitrate conflicts
    /// 4. Execute coordinated actions
    /// 5. Notify agents of results
    /// </summary>
    private async Task ExecuteOptimizationCycleAsync(CancellationToken ct)
    {
        using (await _orchestrationLock.LockAsync(ct).ConfigureAwait(false))
        {
            var cycleStart = DateTime.UtcNow;

            // STEP 1: Gather unified system context
            var context = await _contextStore.GatherContextAsync().ConfigureAwait(false);

            // STEP 2: Collect proposals from all agents in parallel
            var proposalTasks = _agents.Select(agent => GetAgentProposalAsync(agent, context, ct)).ToArray();
            var proposals = await Task.WhenAll(proposalTasks).ConfigureAwait(false);

            // Filter out null/empty proposals
            var validProposals = proposals
                .Where(p => p != null && p.Actions.Count > 0)
                .Cast<AgentProposal>()
                .ToList();

            if (validProposals.Count == 0)
            {
                // No actions needed this cycle
                _totalOptimizationCycles++;
                return;
            }

            // STEP 3: Arbitrate conflicts and create execution plan
            var executionPlan = await _arbitrator.ResolveAsync(validProposals, context).ConfigureAwait(false);

            // STEP 4: Validate execution plan for safety
            if (!_arbitrator.ValidateExecutionPlan(executionPlan, context))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Execution plan failed safety validation - skipping cycle");

                _totalOptimizationCycles++;
                return;
            }

            // STEP 5: Execute coordinated actions
            var contextBefore = context;
            var executionResult = await ExecuteActionsAsync(executionPlan, context, ct).ConfigureAwait(false);

            // STEP 6: Gather post-execution context for learning
            var contextAfter = await _contextStore.GatherContextAsync().ConfigureAwait(false);

            executionResult.ContextBefore = contextBefore;
            executionResult.ContextAfter = contextAfter;
            executionResult.ResolvedConflicts = executionPlan.Conflicts;

            // STEP 7: Notify agents of execution results (for learning)
            var notificationTasks = _agents.Select(agent => NotifyAgentAsync(agent, executionResult, ct)).ToArray();
            await Task.WhenAll(notificationTasks).ConfigureAwait(false);

            // STEP 8: Record behavior for pattern learning (Phase 3)
            if (_behaviorAnalyzer != null && executionResult.ExecutedActions.Count > 0)
            {
                _behaviorAnalyzer.RecordBehavior(contextAfter, executionResult.ExecutedActions);
            }

            // Update metrics
            _totalOptimizationCycles++;
            _totalActionsExecuted += executionResult.ExecutedActions.Count;
            _totalConflictsResolved += executionPlan.Conflicts.Count;

            // Raise completion event
            CycleCompleted?.Invoke(this, new OptimizationCycleCompleted
            {
                CycleNumber = _totalOptimizationCycles,
                Context = context,
                ExecutionPlan = executionPlan,
                ExecutionResult = executionResult,
                Duration = DateTime.UtcNow - cycleStart
            });
        }
    }

    private async Task<AgentProposal?> GetAgentProposalAsync(
        IOptimizationAgent agent,
        SystemContext context,
        CancellationToken ct)
    {
        try
        {
            var proposal = await agent.ProposeActionsAsync(context).ConfigureAwait(false);

            if (proposal.Actions.Count > 0 && Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Agent {agent.AgentName} proposed {proposal.Actions.Count} actions");

            return proposal;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Agent {agent.AgentName} proposal failed", ex);

            return null;
        }
    }

    private async Task<ExecutionResult> ExecuteActionsAsync(
        ExecutionPlan plan,
        SystemContext context,
        CancellationToken ct)
    {
        // Delegate to ActionExecutor for proper handler routing, safety validation, and rollback
        return await _actionExecutor.ExecuteActionsAsync(plan.Actions, context).ConfigureAwait(false);
    }

    private async Task NotifyAgentAsync(
        IOptimizationAgent agent,
        ExecutionResult result,
        CancellationToken ct)
    {
        try
        {
            await agent.OnActionsExecutedAsync(result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Agent {agent.AgentName} notification failed", ex);
        }
    }

    /// <summary>
    /// Dispose the ResourceOrchestrator and release resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose pattern implementation
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Stop the orchestrator first
            StopAsync().GetAwaiter().GetResult();

            // Dispose managed resources
            _cancellationTokenSource?.Dispose();
            _uptimeStopwatch?.Stop();

            // Dispose agents if they implement IDisposable
            foreach (var agent in _agents)
            {
                if (agent is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        _disposed = true;
    }
}

/// <summary>
/// Optimization cycle completion event args
/// </summary>
public class OptimizationCycleCompleted : EventArgs
{
    public long CycleNumber { get; set; }
    public SystemContext Context { get; set; } = null!;
    public ExecutionPlan ExecutionPlan { get; set; } = null!;
    public ExecutionResult ExecutionResult { get; set; } = null!;
    public TimeSpan Duration { get; set; }
}
