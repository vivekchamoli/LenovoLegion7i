using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// System Tick Service - Centralized timer service
/// Consolidates 18+ independent timers into a single master clock
/// Provides multiple tick rates for different use cases
/// </summary>
public class SystemTickService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _tickTask;
    private bool _isRunning;
    private int _tickCount = 0;

    /// <summary>
    /// Fast tick - 500ms (2 Hz) - Aligned with ResourceOrchestrator
    /// Use for: AI agents, orchestrator updates, real-time monitoring
    /// </summary>
    public event EventHandler? FastTick;

    /// <summary>
    /// Medium tick - 1 second (1 Hz)
    /// Use for: UI updates, dashboard controls, status displays
    /// </summary>
    public event EventHandler? MediumTick;

    /// <summary>
    /// Slow tick - 3 seconds
    /// Use for: Battery monitoring, background tasks, periodic checks
    /// </summary>
    public event EventHandler? SlowTick;

    /// <summary>
    /// Very slow tick - 10 seconds
    /// Use for: Infrequent updates, cleanup tasks
    /// </summary>
    public event EventHandler? VerySlowTick;

    /// <summary>
    /// Check if service is running
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Total ticks since start
    /// </summary>
    public int TotalTicks => _tickCount;

    /// <summary>
    /// Start the tick service
    /// Base interval: 500ms (all other intervals are multiples)
    /// </summary>
    public Task StartAsync()
    {
        if (_isRunning)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"System tick service already running");
            return Task.CompletedTask;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting system tick service (base interval: 500ms)");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _tickTask = Task.Run(async () =>
        {
            _isRunning = true;
            _tickCount = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var tickStart = DateTime.UtcNow;

                    // PERFORMANCE FIX: Fire events asynchronously to avoid blocking tick loop
                    // If any subscriber takes too long, it won't delay other ticks
                    _ = Task.Run(() => FastTick?.Invoke(this, EventArgs.Empty));

                    // Medium tick - every 1 second (every 2nd tick)
                    if (_tickCount % 2 == 0)
                    {
                        _ = Task.Run(() => MediumTick?.Invoke(this, EventArgs.Empty));
                    }

                    // Slow tick - every 3 seconds (every 6th tick)
                    if (_tickCount % 6 == 0)
                    {
                        _ = Task.Run(() => SlowTick?.Invoke(this, EventArgs.Empty));
                    }

                    // Very slow tick - every 10 seconds (every 20th tick)
                    if (_tickCount % 20 == 0)
                    {
                        _ = Task.Run(() => VerySlowTick?.Invoke(this, EventArgs.Empty));
                    }

                    _tickCount++;

                    // Calculate next tick time to maintain consistent interval
                    var elapsed = (DateTime.UtcNow - tickStart).TotalMilliseconds;
                    var delay = Math.Max(0, 500 - (int)elapsed);

                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"System tick service error", ex);

                    await Task.Delay(500, token).ConfigureAwait(false);
                }
            }

            _isRunning = false;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"System tick service stopped (total ticks: {_tickCount})");
        }, token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop the tick service
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Stopping system tick service...");

        if (_cts != null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
            _cts = null;
        }

        if (_tickTask != null)
        {
            await _tickTask.ConfigureAwait(false);
            _tickTask = null;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"System tick service stopped");
    }

    /// <summary>
    /// Get tick rate statistics
    /// </summary>
    public string GetStatistics()
    {
        return $"SystemTickService: Running={_isRunning}, TotalTicks={_tickCount}, " +
               $"Subscribers: Fast={FastTick?.GetInvocationList().Length ?? 0}, " +
               $"Medium={MediumTick?.GetInvocationList().Length ?? 0}, " +
               $"Slow={SlowTick?.GetInvocationList().Length ?? 0}, " +
               $"VerySlow={VerySlowTick?.GetInvocationList().Length ?? 0}";
    }

    public void Dispose()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        _tickTask?.Wait(1000); // Wait max 1 second for graceful shutdown
    }
}
