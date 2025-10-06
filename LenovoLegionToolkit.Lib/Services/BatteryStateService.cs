using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Battery State Service - Centralized battery information provider
/// Eliminates duplicate Battery.GetBatteryInformation() calls across the application
/// Updates at 500ms intervals (aligned with ResourceOrchestrator)
/// </summary>
public class BatteryStateService : IDisposable
{
    private BatteryInformation _cachedState;
    private CancellationTokenSource? _cts;
    private Task? _updateTask;
    private bool _isRunning;
    private readonly object _stateLock = new();

    /// <summary>
    /// Fires when battery state changes significantly
    /// </summary>
    public event EventHandler<BatteryInformation>? StateChanged;

    /// <summary>
    /// Get current cached battery state (instant, no WMI call)
    /// </summary>
    public BatteryInformation CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _cachedState;
            }
        }
    }

    /// <summary>
    /// Check if service is running
    /// </summary>
    public bool IsRunning => _isRunning;

    public BatteryStateService()
    {
        // Initialize with current state
        try
        {
            _cachedState = Battery.GetBatteryInformation();
        }
        catch
        {
            // Default state if battery info unavailable
            _cachedState = new BatteryInformation();
        }
    }

    /// <summary>
    /// Start the battery state monitoring service
    /// PERFORMANCE FIX: Update every 2 seconds instead of 500ms
    /// Battery state doesn't change rapidly, 2s is sufficient
    /// </summary>
    public Task StartAsync(int updateIntervalMs = 2000)
    {
        if (_isRunning)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery state service already running");
            return Task.CompletedTask;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting battery state service (interval: {updateIntervalMs}ms)");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _updateTask = Task.Run(async () =>
        {
            _isRunning = true;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var newState = Battery.GetBatteryInformation();

                    bool stateChanged = false;
                    lock (_stateLock)
                    {
                        stateChanged = HasStateChanged(_cachedState, newState);
                        if (stateChanged)
                        {
                            _cachedState = newState;
                        }
                    }

                    // Fire event outside lock to prevent deadlocks
                    if (stateChanged)
                    {
                        StateChanged?.Invoke(this, newState);

                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Battery state changed: {newState.BatteryPercentage}%, Rate: {newState.DischargeRate}mW, Charging: {newState.IsCharging}");
                    }

                    await Task.Delay(updateIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Battery state update failed", ex);

                    await Task.Delay(updateIntervalMs, token).ConfigureAwait(false);
                }
            }

            _isRunning = false;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery state service stopped");
        }, token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop the battery state monitoring service
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Stopping battery state service...");

        if (_cts != null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
            _cts = null;
        }

        if (_updateTask != null)
        {
            await _updateTask.ConfigureAwait(false);
            _updateTask = null;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Battery state service stopped");
    }

    /// <summary>
    /// Determine if battery state has changed significantly
    /// </summary>
    private bool HasStateChanged(BatteryInformation oldState, BatteryInformation newState)
    {
        // Check percentage change (1% threshold)
        if (Math.Abs(oldState.BatteryPercentage - newState.BatteryPercentage) >= 1)
            return true;

        // Check charging state change
        if (oldState.IsCharging != newState.IsCharging)
            return true;

        // Check discharge rate change (5% threshold)
        if (oldState.DischargeRate != 0 && newState.DischargeRate != 0)
        {
            var changePercent = Math.Abs(oldState.DischargeRate - newState.DischargeRate) * 100.0 / oldState.DischargeRate;
            if (changePercent >= 5)
                return true;
        }
        else if (oldState.DischargeRate != newState.DischargeRate)
        {
            return true; // One was zero, now it's not (or vice versa)
        }

        return false;
    }

    /// <summary>
    /// Get discharge rate classification (Phase 2: Discharge rate-aware power)
    /// </summary>
    public DischargeRateLevel GetDischargeRateLevel()
    {
        lock (_stateLock)
        {
            if (_cachedState.IsCharging)
                return DischargeRateLevel.Charging;

            var dischargeRate = _cachedState.DischargeRate;

            // Classify based on discharge rate (mW)
            // Low: < 15W (light productivity, browsing)
            // Medium: 15-30W (normal multitasking, video)
            // High: 30-50W (heavy workload, gaming on battery)
            // Critical: > 50W (unsustainable on battery)

            if (dischargeRate < 15000)
                return DischargeRateLevel.Low;

            if (dischargeRate < 30000)
                return DischargeRateLevel.Medium;

            if (dischargeRate < 50000)
                return DischargeRateLevel.High;

            return DischargeRateLevel.Critical;
        }
    }

    /// <summary>
    /// Get estimated battery time remaining in minutes (Phase 2)
    /// </summary>
    public int GetEstimatedBatteryMinutes()
    {
        lock (_stateLock)
        {
            if (_cachedState.IsCharging || _cachedState.DischargeRate <= 0)
                return -1; // Not discharging or charging

            var currentCapacity = _cachedState.EstimateChargeRemaining; // mWh
            var dischargeRate = _cachedState.DischargeRate; // mW

            var hoursRemaining = (double)currentCapacity / (double)dischargeRate;
            return (int)(hoursRemaining * 60);
        }
    }

    /// <summary>
    /// Should reduce power consumption? (Phase 2: Predictive power management)
    /// </summary>
    public bool ShouldReducePower()
    {
        lock (_stateLock)
        {
            // Not on battery - no need to reduce
            if (_cachedState.IsCharging)
                return false;

            var level = GetDischargeRateLevel();
            var chargePercent = _cachedState.BatteryPercentage;

            // Critical discharge or low battery - must reduce
            if (level == DischargeRateLevel.Critical || chargePercent < 20)
                return true;

            // High discharge with medium battery - should reduce
            if (level == DischargeRateLevel.High && chargePercent < 40)
                return true;

            // Medium discharge with low-ish battery - consider reducing
            if (level == DischargeRateLevel.Medium && chargePercent < 30)
                return true;

            return false;
        }
    }

    public void Dispose()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        _updateTask?.Wait(1000); // Wait max 1 second for graceful shutdown
    }
}

/// <summary>
/// Discharge Rate Classification (Phase 2)
/// </summary>
public enum DischargeRateLevel
{
    /// <summary>Battery is charging</summary>
    Charging,

    /// <summary>Low discharge (< 15W) - light tasks, browsing</summary>
    Low,

    /// <summary>Medium discharge (15-30W) - normal multitasking, video</summary>
    Medium,

    /// <summary>High discharge (30-50W) - heavy workload, gaming on battery</summary>
    High,

    /// <summary>Critical discharge (> 50W) - unsustainable, will drain quickly</summary>
    Critical
}
