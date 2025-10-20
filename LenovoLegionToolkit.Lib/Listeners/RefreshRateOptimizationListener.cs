using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Services;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

/// <summary>
/// Refresh Rate Optimization Listener
/// Automatically triggers refresh rate optimization on system state changes
/// PHASE 1: Power-aware refresh rate automation
/// ELITE SECURITY FIX: Debouncing and synchronization to prevent race conditions
/// </summary>
public class RefreshRateOptimizationListener
{
    private readonly RefreshRateFeature _refreshRateFeature;
    private readonly PowerModeListener _powerModeListener;
    private readonly PowerStateListener _powerStateListener;
    private readonly BatteryStateService? _batteryStateService;

    private PowerModeState _lastPowerMode = PowerModeState.Balance;
    private bool _lastWasOnBattery = false;
    private int _lastBatteryPercent = 100;

    // ELITE FIX: Debouncing and synchronization
    private readonly SemaphoreSlim _optimizationLock = new(1, 1);
    private DateTime _lastOptimization = DateTime.MinValue;
    private const int DEBOUNCE_MS = 1000; // 1 second debounce to prevent rapid-fire changes

    public RefreshRateOptimizationListener(
        RefreshRateFeature refreshRateFeature,
        PowerModeListener powerModeListener,
        PowerStateListener powerStateListener,
        BatteryStateService? batteryStateService = null)
    {
        _refreshRateFeature = refreshRateFeature ?? throw new ArgumentNullException(nameof(refreshRateFeature));
        _powerModeListener = powerModeListener ?? throw new ArgumentNullException(nameof(powerModeListener));
        _powerStateListener = powerStateListener ?? throw new ArgumentNullException(nameof(powerStateListener));
        _batteryStateService = batteryStateService;

        // Subscribe to events
        _powerModeListener.Changed += OnPowerModeChanged;
        _powerStateListener.Changed += OnPowerStateChanged;

        if (_batteryStateService != null)
        {
            _batteryStateService.StateChanged += OnBatteryStateChanged;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"RefreshRateOptimizationListener initialized");
    }

    /// <summary>
    /// Handle power mode changes
    /// ELITE SECURITY FIX: Debouncing and locking to prevent race conditions
    /// </summary>
    private async void OnPowerModeChanged(object? sender, PowerModeListener.ChangedEventArgs e)
    {
        try
        {
            var powerMode = e.State;

            if (_lastPowerMode == powerMode)
                return;

            _lastPowerMode = powerMode;

            // ELITE FIX: Debounce - skip if last optimization was within debounce window
            if ((DateTime.Now - _lastOptimization).TotalMilliseconds < DEBOUNCE_MS)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Power mode changed to {powerMode} - debounced (too soon after last optimization)");
                return;
            }

            // ELITE FIX: Lock - only one optimization at a time
            if (!await _optimizationLock.WaitAsync(0).ConfigureAwait(false))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Power mode changed to {powerMode} - skipped (optimization already in progress)");
                return;
            }

            try
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Power mode changed to {powerMode} - triggering refresh rate optimization");

                await TriggerOptimizationAsync("Power mode change").ConfigureAwait(false);
                _lastOptimization = DateTime.Now;
            }
            finally
            {
                _optimizationLock.Release();
            }
        }
        catch (Exception ex)
        {
            // ELITE FIX: Catch exceptions from async void to prevent app crash
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power mode changed handler failed (non-critical)", ex);
        }
    }

    /// <summary>
    /// Handle power state changes (AC/Battery)
    /// ELITE SECURITY FIX: Debouncing and locking to prevent race conditions
    /// </summary>
    private async void OnPowerStateChanged(object? sender, PowerStateListener.ChangedEventArgs e)
    {
        try
        {
            var isOnBattery = e.PowerAdapterStateChanged;

            if (_lastWasOnBattery == isOnBattery)
                return;

            _lastWasOnBattery = isOnBattery;

            // ELITE FIX: Debounce - skip if last optimization was within debounce window
            if ((DateTime.Now - _lastOptimization).TotalMilliseconds < DEBOUNCE_MS)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Power state changed to {(isOnBattery ? "Battery" : "AC")} - debounced (too soon after last optimization)");
                return;
            }

            // ELITE FIX: Lock - only one optimization at a time
            if (!await _optimizationLock.WaitAsync(0).ConfigureAwait(false))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Power state changed to {(isOnBattery ? "Battery" : "AC")} - skipped (optimization already in progress)");
                return;
            }

            try
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Power state changed to {(isOnBattery ? "Battery" : "AC")} - triggering refresh rate optimization");

                await TriggerOptimizationAsync("Power state change").ConfigureAwait(false);
                _lastOptimization = DateTime.Now;
            }
            finally
            {
                _optimizationLock.Release();
            }
        }
        catch (Exception ex)
        {
            // ELITE FIX: Catch exceptions from async void to prevent app crash
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power state changed handler failed (non-critical)", ex);
        }
    }

    /// <summary>
    /// Handle battery state changes
    /// ELITE SECURITY FIX: Debouncing and locking to prevent race conditions
    /// </summary>
    private async void OnBatteryStateChanged(object? sender, BatteryInformation batteryInfo)
    {
        try
        {
            // Trigger optimization on significant battery changes (10% threshold)
            var batteryDelta = Math.Abs(batteryInfo.BatteryPercentage - _lastBatteryPercent);

            if (batteryDelta >= 10)
            {
                _lastBatteryPercent = batteryInfo.BatteryPercentage;

                // ELITE FIX: Debounce - skip if last optimization was within debounce window
                if ((DateTime.Now - _lastOptimization).TotalMilliseconds < DEBOUNCE_MS)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Battery level changed to {batteryInfo.BatteryPercentage}% - debounced (too soon after last optimization)");
                    return;
                }

                // ELITE FIX: Lock - only one optimization at a time
                if (!await _optimizationLock.WaitAsync(0).ConfigureAwait(false))
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Battery level changed to {batteryInfo.BatteryPercentage}% - skipped (optimization already in progress)");
                    return;
                }

                try
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Battery level changed to {batteryInfo.BatteryPercentage}% - triggering refresh rate optimization");

                    await TriggerOptimizationAsync($"Battery {batteryInfo.BatteryPercentage}%").ConfigureAwait(false);
                    _lastOptimization = DateTime.Now;
                }
                finally
                {
                    _optimizationLock.Release();
                }
            }
        }
        catch (Exception ex)
        {
            // ELITE FIX: Catch exceptions from async void to prevent app crash
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery state changed handler failed (non-critical)", ex);
        }
    }

    /// <summary>
    /// Trigger refresh rate optimization
    /// </summary>
    private async Task TriggerOptimizationAsync(string reason)
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Triggering refresh rate optimization: {reason}");

            await _refreshRateFeature.ApplyOptimalRefreshRateAsync().ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Refresh rate optimization completed: {reason}");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Refresh rate optimization failed: {reason}", ex);
        }
    }

    /// <summary>
    /// Unsubscribe from events
    /// </summary>
    public void Dispose()
    {
        _powerModeListener.Changed -= OnPowerModeChanged;
        _powerStateListener.Changed -= OnPowerStateChanged;

        if (_batteryStateService != null)
        {
            _batteryStateService.StateChanged -= OnBatteryStateChanged;
        }

        _optimizationLock?.Dispose(); // ELITE FIX: Dispose lock

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"RefreshRateOptimizationListener disposed");
    }
}
