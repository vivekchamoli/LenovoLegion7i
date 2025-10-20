using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Refresh Rate Optimizer - Automatic refresh rate adjustment based on system state
/// PHASE 1 OPTIMIZATION: Autonomous power-aware refresh rate control
/// Target: 2-4W battery savings by intelligently reducing refresh rate
/// ELITE SECURITY FIX: Debouncing and synchronization to prevent race conditions
/// </summary>
public class RefreshRateOptimizer : IDisposable
{
    private readonly RefreshRateFeature _refreshRateFeature;
    private readonly PowerModeListener _powerModeListener;
    private readonly BatteryStateService _batteryStateService;
    private readonly GPUController _gpuController;
    private readonly AI.CoolingPeriodManager? _coolingPeriodManager; // CRITICAL FIX v6.20.12: Respect user overrides

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private volatile bool _isRunning; // CRITICAL FIX v6.20.10: volatile prevents compiler/CPU reordering across threads
    private volatile bool _isEnabled = true; // CRITICAL FIX v6.20.10: volatile prevents compiler/CPU reordering across threads

    private int _lastBatteryPercent = -1;
    private PowerModeState _lastPowerMode = PowerModeState.Balance;
    private GPUState _lastGPUState = GPUState.Unknown;

    // ELITE FIX: Debouncing and synchronization
    private readonly SemaphoreSlim _optimizationLock = new(1, 1);
    private DateTime _lastOptimization = DateTime.MinValue;
    private const int DEBOUNCE_MS = 1000; // 1 second debounce to prevent rapid-fire changes

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"RefreshRateOptimizer {(_isEnabled ? "enabled" : "disabled")}");
        }
    }

    public RefreshRateOptimizer(
        RefreshRateFeature refreshRateFeature,
        PowerModeListener powerModeListener,
        BatteryStateService batteryStateService,
        GPUController gpuController,
        AI.CoolingPeriodManager? coolingPeriodManager = null) // CRITICAL FIX v6.20.12: Optional cooling period manager
    {
        _refreshRateFeature = refreshRateFeature ?? throw new ArgumentNullException(nameof(refreshRateFeature));
        _powerModeListener = powerModeListener ?? throw new ArgumentNullException(nameof(powerModeListener));
        _batteryStateService = batteryStateService ?? throw new ArgumentNullException(nameof(batteryStateService));
        _gpuController = gpuController ?? throw new ArgumentNullException(nameof(gpuController));
        _coolingPeriodManager = coolingPeriodManager; // Optional - graceful degradation if null
    }

    /// <summary>
    /// Start automatic refresh rate optimization
    /// </summary>
    public Task StartAsync()
    {
        if (_isRunning)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"RefreshRateOptimizer already running");
            return Task.CompletedTask;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting RefreshRateOptimizer...");

        // Subscribe to events
        _powerModeListener.Changed += OnPowerModeChanged;
        _batteryStateService.StateChanged += OnBatteryStateChanged;
        _gpuController.Refreshed += OnGPUStateChanged;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _monitorTask = Task.Run(async () =>
        {
            _isRunning = true;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"RefreshRateOptimizer started");

            // Initial optimization
            await ApplyOptimalRefreshRateAsync().ConfigureAwait(false);

            // Keep monitoring active (events will trigger adjustments)
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _isRunning = false;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"RefreshRateOptimizer stopped");
        }, token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop automatic refresh rate optimization
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Stopping RefreshRateOptimizer...");

        // Unsubscribe from events
        _powerModeListener.Changed -= OnPowerModeChanged;
        _batteryStateService.StateChanged -= OnBatteryStateChanged;
        _gpuController.Refreshed -= OnGPUStateChanged;

        if (_cts != null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
            _cts = null;
        }

        if (_monitorTask != null)
        {
            await _monitorTask.ConfigureAwait(false);
            _monitorTask = null;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"RefreshRateOptimizer stopped");
    }

    /// <summary>
    /// Handle power mode changes
    /// ELITE SECURITY FIX: Debouncing and locking to prevent race conditions
    /// </summary>
    private async void OnPowerModeChanged(object? sender, PowerModeListener.ChangedEventArgs e)
    {
        var powerMode = e.State;

        if (!_isEnabled || _lastPowerMode == powerMode)
            return;

        _lastPowerMode = powerMode;

        // ELITE FIX: Debounce - skip if last optimization was within debounce window
        if ((DateTime.Now - _lastOptimization).TotalMilliseconds < DEBOUNCE_MS)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power mode changed to {powerMode} - debounced (too soon after last optimization)");
            return;
        }

        // ELITE FIX: Lock - only one optimization at a time (prevents concurrent display changes)
        if (!await _optimizationLock.WaitAsync(0).ConfigureAwait(false))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power mode changed to {powerMode} - skipped (optimization already in progress)");
            return;
        }

        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power mode changed to {powerMode} - applying optimal refresh rate");

            await ApplyOptimalRefreshRateAsync().ConfigureAwait(false);
            _lastOptimization = DateTime.Now;
        }
        finally
        {
            _optimizationLock.Release();
        }
    }

    /// <summary>
    /// Handle battery state changes
    /// ELITE SECURITY FIX: Debouncing and locking to prevent race conditions
    /// </summary>
    private async void OnBatteryStateChanged(object? sender, BatteryInformation batteryInfo)
    {
        if (!_isEnabled)
            return;

        // Only react to significant battery changes (10% threshold)
        if (Math.Abs(batteryInfo.BatteryPercentage - _lastBatteryPercent) < 10)
            return;

        _lastBatteryPercent = batteryInfo.BatteryPercentage;

        // ELITE FIX: Debounce - skip if last optimization was within debounce window
        if ((DateTime.Now - _lastOptimization).TotalMilliseconds < DEBOUNCE_MS)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery state changed to {batteryInfo.BatteryPercentage}% - debounced (too soon after last optimization)");
            return;
        }

        // ELITE FIX: Lock - only one optimization at a time
        if (!await _optimizationLock.WaitAsync(0).ConfigureAwait(false))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery state changed to {batteryInfo.BatteryPercentage}% - skipped (optimization already in progress)");
            return;
        }

        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Battery state changed to {batteryInfo.BatteryPercentage}% - applying optimal refresh rate");

            await ApplyOptimalRefreshRateAsync().ConfigureAwait(false);
            _lastOptimization = DateTime.Now;
        }
        finally
        {
            _optimizationLock.Release();
        }
    }

    /// <summary>
    /// Handle GPU state changes
    /// ELITE SECURITY FIX: Debouncing and locking to prevent race conditions
    /// </summary>
    private async void OnGPUStateChanged(object? sender, GPUStatus gpuStatus)
    {
        if (!_isEnabled || _lastGPUState == gpuStatus.State)
            return;

        _lastGPUState = gpuStatus.State;

        // ELITE FIX: Debounce - skip if last optimization was within debounce window
        if ((DateTime.Now - _lastOptimization).TotalMilliseconds < DEBOUNCE_MS)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU state changed to {gpuStatus.State} - debounced (too soon after last optimization)");
            return;
        }

        // ELITE FIX: Lock - only one optimization at a time
        if (!await _optimizationLock.WaitAsync(0).ConfigureAwait(false))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU state changed to {gpuStatus.State} - skipped (optimization already in progress)");
            return;
        }

        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU state changed to {gpuStatus.State} - applying optimal refresh rate");

            await ApplyOptimalRefreshRateAsync().ConfigureAwait(false);
            _lastOptimization = DateTime.Now;
        }
        finally
        {
            _optimizationLock.Release();
        }
    }

    /// <summary>
    /// Apply optimal refresh rate based on current system state
    /// CRITICAL FIX v6.20.12: Now respects user override cooling periods
    /// </summary>
    private async Task ApplyOptimalRefreshRateAsync()
    {
        if (!_isEnabled)
            return;

        // CRITICAL FIX v6.20.12: Check if user has manually set refresh rate
        if (_coolingPeriodManager != null)
        {
            if (_coolingPeriodManager.IsInCoolingPeriod("DISPLAY_REFRESH_RATE", out var remaining))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Refresh rate optimization skipped - user override active ({remaining.TotalMinutes:F1}min remaining)");
                return; // Respect user's manual refresh rate setting
            }
        }

        try
        {
            await _refreshRateFeature.ApplyOptimalRefreshRateAsync().ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Optimal refresh rate applied successfully");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply optimal refresh rate", ex);
        }
    }

    public void Dispose()
    {
        StopAsync().Wait(1000);
        _optimizationLock?.Dispose(); // ELITE FIX: Dispose lock
    }
}
