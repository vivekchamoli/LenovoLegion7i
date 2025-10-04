using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// Action Executor - Bridges agent proposals to actual hardware control
/// Executes resource actions with safety validation and rollback capability
/// </summary>
public class ActionExecutor
{
    private readonly Dictionary<string, IActionHandler> _handlers = new();
    private readonly SafetyValidator _safetyValidator;

    public ActionExecutor(
        SafetyValidator safetyValidator,
        IEnumerable<IActionHandler> handlers)
    {
        _safetyValidator = safetyValidator ?? throw new ArgumentNullException(nameof(safetyValidator));

        if (handlers == null)
            throw new ArgumentNullException(nameof(handlers));

        // Register all handlers
        foreach (var handler in handlers)
        {
            foreach (var target in handler.SupportedTargets)
            {
                _handlers[target] = handler;
            }
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"ActionExecutor initialized with {_handlers.Count} action handlers");
    }

    /// <summary>
    /// Execute a list of actions with safety validation and error handling
    /// </summary>
    public async Task<ExecutionResult> ExecuteActionsAsync(
        List<ResourceAction> actions,
        SystemContext contextBefore)
    {
        var executedActions = new List<ResourceAction>();
        var failedActions = new List<string>();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Executing {actions.Count} actions...");

        // Sort by priority: Critical > Emergency > Proactive > Opportunistic
        var sortedActions = actions.OrderBy(a => GetPriorityValue(a.Type)).ToList();

        foreach (var action in sortedActions)
        {
            try
            {
                // Safety validation
                var validation = _safetyValidator.ValidateAction(action, contextBefore);
                if (!validation.IsAllowed)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Action rejected by safety validator: {action.Target} - {validation.Reason}");

                    failedActions.Add($"{action.Target}: {validation.Reason}");
                    continue;
                }

                // Get handler
                if (!_handlers.TryGetValue(action.Target, out var handler))
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"No handler found for target: {action.Target}");

                    failedActions.Add($"{action.Target}: No handler registered");
                    continue;
                }

                // Execute action
                await handler.ExecuteAsync(action).ConfigureAwait(false);

                executedActions.Add(action);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Action executed: {action.Target} = {action.Value} ({action.Reason})");
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Action execution failed: {action.Target} - {ex.Message}");

                failedActions.Add($"{action.Target}: {ex.Message}");

                // If critical action fails, rollback and abort
                if (action.Type == ActionType.Critical)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Critical action failed - initiating rollback");

                    await RollbackActionsAsync(executedActions).ConfigureAwait(false);

                    return new ExecutionResult
                    {
                        Success = false,
                        ExecutedActions = new List<ResourceAction>(), // Rolled back
                        ContextBefore = contextBefore,
                        ContextAfter = contextBefore, // No change due to rollback
                        ResolvedConflicts = new List<Conflict>(),
                        Metrics = new Dictionary<string, object>
                        {
                            ["RollbackPerformed"] = true,
                            ["FailedActions"] = failedActions
                        }
                    };
                }
            }
        }

        var success = failedActions.Count == 0 && executedActions.Count > 0;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Execution complete: {executedActions.Count}/{actions.Count} actions executed, Success={success}");

        return new ExecutionResult
        {
            Success = success,
            ExecutedActions = executedActions,
            ContextBefore = contextBefore,
            ContextAfter = contextBefore, // Will be updated by orchestrator
            ResolvedConflicts = new List<Conflict>(),
            Metrics = new Dictionary<string, object>
            {
                ["FailedActions"] = failedActions,
                ["ExecutionCount"] = executedActions.Count
            }
        };
    }

    /// <summary>
    /// Rollback previously executed actions
    /// </summary>
    private async Task RollbackActionsAsync(List<ResourceAction> executedActions)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Rolling back {executedActions.Count} actions...");

        // Rollback in reverse order
        foreach (var action in executedActions.AsEnumerable().Reverse())
        {
            try
            {
                if (_handlers.TryGetValue(action.Target, out var handler))
                {
                    await handler.RollbackAsync(action).ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Rolled back: {action.Target}");
                }
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Rollback failed for {action.Target}: {ex.Message}");
                // Continue rolling back other actions
            }
        }
    }

    private static int GetPriorityValue(ActionType type) => type switch
    {
        ActionType.Critical => 0,
        ActionType.Emergency => 1,
        ActionType.Proactive => 2,
        ActionType.Opportunistic => 3,
        _ => 4
    };
}

/// <summary>
/// Interface for action handlers
/// </summary>
public interface IActionHandler
{
    /// <summary>
    /// List of action targets this handler can process
    /// </summary>
    string[] SupportedTargets { get; }

    /// <summary>
    /// Execute an action
    /// </summary>
    Task ExecuteAsync(ResourceAction action);

    /// <summary>
    /// Rollback an action (restore previous state)
    /// </summary>
    Task RollbackAsync(ResourceAction action);
}

/// <summary>
/// Result of individual action execution (internal tracking)
/// </summary>
internal class ActionExecutionResult
{
    public ResourceAction Action { get; set; } = null!;
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutionTime { get; set; }
}
