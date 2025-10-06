using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Utils;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// GPU Transition Manager - Thread-safe GPU mode switching with cost awareness
/// Fixes race conditions, implements minimum dwell time, tracks transition overhead
/// Phase 1 Critical Fix from Elite GPU/Power Analysis
/// </summary>
public class GPUTransitionManager
{
    private readonly HybridModeFeature _hybridModeFeature;
    private readonly AsyncLock _transitionLock = new();

    private HybridModeState _lastKnownState;
    private DateTime _lastTransitionTime = DateTime.MinValue;
    private bool _isTransitionInProgress = false;

    // Configuration
    private readonly TimeSpan _minimumDwellTime = TimeSpan.FromMinutes(5); // Prevent GPU thrashing
    private readonly TimeSpan _transitionCostEstimate = TimeSpan.FromSeconds(2); // GPU mode switch overhead

    // Statistics
    private int _transitionCount = 0;
    private TimeSpan _totalBlockedTime = TimeSpan.Zero;

    public GPUTransitionManager(HybridModeFeature hybridModeFeature)
    {
        _hybridModeFeature = hybridModeFeature ?? throw new ArgumentNullException(nameof(hybridModeFeature));
    }

    /// <summary>
    /// Get current GPU mode with thread safety
    /// </summary>
    public async Task<HybridModeState> GetCurrentStateAsync()
    {
        using (await _transitionLock.LockAsync().ConfigureAwait(false))
        {
            // Update cached state from hardware
            _lastKnownState = await _hybridModeFeature.GetStateAsync().ConfigureAwait(false);
            return _lastKnownState;
        }
    }

    /// <summary>
    /// Propose GPU mode transition with cost awareness
    /// Returns null if transition should be blocked (minimum dwell time not met)
    /// </summary>
    public async Task<GPUTransitionProposal?> ProposeTransitionAsync(
        HybridModeState targetMode,
        string reason,
        TransitionPriority priority = TransitionPriority.Normal)
    {
        using (await _transitionLock.LockAsync().ConfigureAwait(false))
        {
            // Get current state
            var currentMode = await _hybridModeFeature.GetStateAsync().ConfigureAwait(false);
            _lastKnownState = currentMode;

            // No change needed
            if (currentMode == targetMode)
                return null;

            // Check if transition is already in progress
            if (_isTransitionInProgress)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"GPU transition blocked: Another transition in progress");

                return null;
            }

            // Check minimum dwell time (unless Critical priority)
            var timeSinceLastTransition = DateTime.Now - _lastTransitionTime;
            if (priority != TransitionPriority.Critical &&
                timeSinceLastTransition < _minimumDwellTime)
            {
                var remainingDwellTime = _minimumDwellTime - timeSinceLastTransition;

                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"GPU transition blocked: Minimum dwell time not met. Remaining: {remainingDwellTime.TotalSeconds:F0}s (from={currentMode}, to={targetMode})");
                }

                _totalBlockedTime += remainingDwellTime;

                return new GPUTransitionProposal
                {
                    CurrentMode = currentMode,
                    TargetMode = targetMode,
                    IsBlocked = true,
                    BlockReason = $"Minimum dwell time ({_minimumDwellTime.TotalMinutes}min) not met",
                    RemainingDwellTime = remainingDwellTime,
                    Reason = reason
                };
            }

            // Transition is allowed
            return new GPUTransitionProposal
            {
                CurrentMode = currentMode,
                TargetMode = targetMode,
                IsBlocked = false,
                EstimatedCost = _transitionCostEstimate,
                Reason = reason,
                Priority = priority
            };
        }
    }

    /// <summary>
    /// Execute GPU mode transition (thread-safe, atomic operation)
    /// </summary>
    public async Task<bool> ExecuteTransitionAsync(GPUTransitionProposal proposal)
    {
        if (proposal.IsBlocked)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Cannot execute blocked transition: {proposal.BlockReason}");
            return false;
        }

        using (await _transitionLock.LockAsync().ConfigureAwait(false))
        {
            // Double-check state hasn't changed since proposal
            var currentMode = await _hybridModeFeature.GetStateAsync().ConfigureAwait(false);
            if (currentMode != proposal.CurrentMode)
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"GPU transition cancelled: State changed from {proposal.CurrentMode} to {currentMode} (target was {proposal.TargetMode})");
                }

                _lastKnownState = currentMode;
                return false;
            }

            // Mark transition in progress
            _isTransitionInProgress = true;

            try
            {
                var startTime = DateTime.Now;

                // Execute transition
                await _hybridModeFeature.SetStateAsync(proposal.TargetMode).ConfigureAwait(false);

                var actualCost = DateTime.Now - startTime;
                _transitionCount++;
                _lastTransitionTime = DateTime.Now;
                _lastKnownState = proposal.TargetMode;

                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"GPU transition executed: {proposal.CurrentMode} → {proposal.TargetMode} (cost: {actualCost.TotalSeconds:F2}s, reason: {proposal.Reason})");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"GPU transition failed: {proposal.CurrentMode} → {proposal.TargetMode}", ex);

                return false;
            }
            finally
            {
                _isTransitionInProgress = false;
            }
        }
    }

    /// <summary>
    /// Force transition bypass (for critical situations like critical battery)
    /// </summary>
    public async Task<bool> ForceTransitionAsync(HybridModeState targetMode, string reason)
    {
        var proposal = await ProposeTransitionAsync(targetMode, reason, TransitionPriority.Critical).ConfigureAwait(false);

        if (proposal == null)
            return false;

        return await ExecuteTransitionAsync(proposal).ConfigureAwait(false);
    }

    /// <summary>
    /// Get transition statistics
    /// </summary>
    public GPUTransitionStats GetStatistics()
    {
        return new GPUTransitionStats
        {
            TransitionCount = _transitionCount,
            LastTransitionTime = _lastTransitionTime,
            TotalBlockedTime = _totalBlockedTime,
            LastKnownState = _lastKnownState,
            MinimumDwellTime = _minimumDwellTime,
            EstimatedTransitionCost = _transitionCostEstimate
        };
    }
}

/// <summary>
/// GPU Transition Proposal - Result of transition feasibility check
/// </summary>
public class GPUTransitionProposal
{
    public HybridModeState CurrentMode { get; set; }
    public HybridModeState TargetMode { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }
    public TimeSpan? RemainingDwellTime { get; set; }
    public TimeSpan EstimatedCost { get; set; }
    public string Reason { get; set; } = string.Empty;
    public TransitionPriority Priority { get; set; }
}

/// <summary>
/// GPU Transition Statistics
/// </summary>
public class GPUTransitionStats
{
    public int TransitionCount { get; set; }
    public DateTime LastTransitionTime { get; set; }
    public TimeSpan TotalBlockedTime { get; set; }
    public HybridModeState LastKnownState { get; set; }
    public TimeSpan MinimumDwellTime { get; set; }
    public TimeSpan EstimatedTransitionCost { get; set; }
}

/// <summary>
/// Transition Priority Levels
/// </summary>
public enum TransitionPriority
{
    /// <summary>Normal priority - respects minimum dwell time</summary>
    Normal,

    /// <summary>High priority - reduces dwell time to 2 minutes</summary>
    High,

    /// <summary>Critical - bypasses dwell time (battery &lt; 15%, external monitor, etc.)</summary>
    Critical
}
