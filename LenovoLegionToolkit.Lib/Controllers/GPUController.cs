using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Resources;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.Controllers;

public class GPUController : IDisposable
{
    private readonly AsyncLock _lock = new();

    private Task? _refreshTask;
    private CancellationTokenSource? _refreshCancellationTokenSource;

    private GPUState _state = GPUState.Unknown;
    private List<Process> _processes = [];
    private string? _gpuInstanceId;
    private string? _performanceState;
    private string? _deviceName;

    // CRITICAL FIX v6.20.20: Cache NVAPI availability to prevent 1,700+ polling storm
    // System was checking IsSupported() every call, causing repeated NVAPI initialization attempts
    // Cache result for 60 seconds - only re-check on Hybrid Mode state changes
    private static bool? _nvapiSupportedCache = null;
    private static DateTime _nvapiCacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan NVAPI_CACHE_DURATION = TimeSpan.FromSeconds(60);

    public event EventHandler<GPUStatus>? Refreshed;
    public bool IsStarted { get => _refreshTask != null; }

    public bool IsSupported()
    {
        // CRITICAL FIX v6.20.20: Check cache first to prevent 1,700+ NVAPI initialization attempts
        // Cache valid for 60 seconds - dramatically reduces CPU wake-ups and power consumption
        var now = DateTime.UtcNow;
        if (_nvapiSupportedCache.HasValue && now < _nvapiCacheExpiry)
        {
            return _nvapiSupportedCache.Value;
        }

        // Cache miss or expired - perform actual check
        bool result;
        try
        {
            NVAPI.Initialize();
            var gpuDetected = NVAPI.GetGPU() is not null;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU hardware detection: {(gpuDetected ? "NVIDIA GPU found" : "No NVIDIA GPU")} [cache refreshed]");

            result = gpuDetected;
        }
        catch (Exception ex)
        {
            // CRITICAL FIX v6.20.17: Check if this is a powered-off GPU (still supported) vs no GPU hardware
            // NVAPI_NVIDIA_DEVICE_NOT_FOUND can mean:
            // 1. dGPU is powered off (hybrid mode) - SHOULD show control with "Powered Off" status
            // 2. No NVIDIA hardware at all - SHOULD hide control

            // If HybridModeFeature is available, system has dGPU hardware (even if powered off)
            var hasHybridMode = false;
            try
            {
                var hybridModeFeature = IoCContainer.TryResolve<HybridModeFeature>();
                hasHybridMode = hybridModeFeature is not null;
            }
            catch
            {
                hasHybridMode = false;
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"NVAPI unavailable - HybridMode available: {hasHybridMode}, Error: {ex.Message} [cache refreshed]");

            // If hybrid mode is available, system has dGPU (just powered off)
            result = hasHybridMode;
        }
        finally
        {
            try
            {
                NVAPI.Unload();
            }
            catch { /* Ignored. */ }
        }

        // Update cache
        _nvapiSupportedCache = result;
        _nvapiCacheExpiry = now.Add(NVAPI_CACHE_DURATION);

        return result;
    }

    /// <summary>
    /// CRITICAL FIX v6.20.20: Invalidate NVAPI cache when Hybrid Mode state changes
    /// Call this method after GPU mode transitions to force re-detection
    /// </summary>
    public static void InvalidateNVAPICache()
    {
        _nvapiSupportedCache = null;
        _nvapiCacheExpiry = DateTime.MinValue;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"NVAPI cache invalidated - will re-check on next IsSupported() call");
    }

    public async Task<GPUState> GetLastKnownStateAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
            return _state;
    }

    public async Task<GPUStatus> RefreshNowAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            await RefreshLoopAsync(0, 0, CancellationToken.None).ConfigureAwait(false);
            return new GPUStatus(_state, _performanceState, _processes, _deviceName);
        }
    }

    public Task StartAsync(int delay = 1_000, int interval = 5_000)
    {
        if (IsStarted)
            return Task.CompletedTask;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting... [delay={delay}, interval={interval}]");

        _refreshCancellationTokenSource = new CancellationTokenSource();
        var token = _refreshCancellationTokenSource.Token;
        _refreshTask = Task.Run(() => RefreshLoopAsync(delay, interval, token), token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(bool waitForFinish = false)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Stopping... [refreshTask.isNull={_refreshTask is null}, _refreshCancellationTokenSource.IsCancellationRequested={_refreshCancellationTokenSource?.IsCancellationRequested}]");

        if (_refreshCancellationTokenSource is not null)
            await _refreshCancellationTokenSource.CancelAsync().ConfigureAwait(false);

        if (waitForFinish)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Waiting to finish...");

            if (_refreshTask is not null)
            {
                try
                {
                    await _refreshTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Finished");
        }

        _refreshCancellationTokenSource = null;
        _refreshTask = null;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Stopped");
    }

    public async Task RestartGPUAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Deactivating... [state={_state}, gpuInstanceId={_gpuInstanceId}]");

            if (_state is not GPUState.Active and not GPUState.Inactive)
                return;

            if (string.IsNullOrEmpty(_gpuInstanceId))
                return;

            await CMD.RunAsync("pnputil", $"/restart-device \"{_gpuInstanceId}\"").ConfigureAwait(false);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Deactivating... [state= {_state}, gpuInstanceId={_gpuInstanceId}]");
        }
    }

    public async Task KillGPUProcessesAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Deactivating... [state= {_state}, gpuInstanceId={_gpuInstanceId}]");

            if (_state is not GPUState.Active)
                return;

            if (string.IsNullOrEmpty(_gpuInstanceId))
                return;

            foreach (var process in _processes)
            {
                try
                {
                    process.Kill(true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Couldn't kill process. [pid={process.Id}, name={process.ProcessName}]", ex);
                }
            }

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Deactivating... [state=  {_state}, gpuInstanceId={_gpuInstanceId}]");
        }
    }

    private async Task RefreshLoopAsync(int delay, int interval, CancellationToken token)
    {
        try
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Initializing NVAPI...");

            try
            {
                NVAPI.Initialize();

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Initialized NVAPI");
            }
            catch (Exception)
            {
                // CRITICAL FIX v6.20.17: Graceful degradation when NVAPI unavailable (iGPU mode, driver issues)
                // Check if system has dGPU (hybrid mode) to distinguish powered-off vs not present
                var hasHybridMode = false;
                try
                {
                    var hybridModeFeature = IoCContainer.TryResolve<HybridModeFeature>();
                    hasHybridMode = hybridModeFeature is not null;
                }
                catch
                {
                    hasHybridMode = false;
                }

                if (hasHybridMode)
                {
                    // System has dGPU hardware - it's just powered off (iGPU-only mode)
                    _state = GPUState.PoweredOff;
                    _performanceState = Resource.GPUController_PoweredOff;

                    // CRITICAL FIX v6.22.1: Don't log NVAPI exception in iGPU mode - it's expected behavior
                    // Logging exception causes 200+ "=== Exception ===" lines in log (log pollution)
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"NVAPI initialization failed - dGPU powered off (iGPU-only mode)");
                }
                else
                {
                    // No NVIDIA GPU hardware detected
                    _state = GPUState.NvidiaGpuNotFound;

                    // CRITICAL FIX v6.22.1: Don't log NVAPI exception when no GPU - it's expected behavior
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"NVAPI initialization failed - No NVIDIA GPU detected");
                }

                Refreshed?.Invoke(this, new GPUStatus(_state, _performanceState, _processes, _deviceName));
                return; // Exit gracefully without throwing
            }

            await Task.Delay(delay, token).ConfigureAwait(false);

            while (true)
            {
                token.ThrowIfCancellationRequested();

                using (await _lock.LockAsync(token).ConfigureAwait(false))
                {

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Will refresh...");

                    await RefreshStateAsync().ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Refreshed");

                    Refreshed?.Invoke(this, new GPUStatus(_state, _performanceState, _processes, _deviceName));
                }

                if (interval > 0)
                    await Task.Delay(interval, token).ConfigureAwait(false);
                else
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Exception occurred", ex);

            // CRITICAL FIX v6.20.17: Don't throw - distinguish powered-off vs not present
            var hasHybridMode = false;
            try
            {
                var hybridModeFeature = IoCContainer.TryResolve<HybridModeFeature>();
                hasHybridMode = hybridModeFeature is not null;
            }
            catch
            {
                hasHybridMode = false;
            }

            if (hasHybridMode)
            {
                _state = GPUState.PoweredOff;
                _performanceState = Resource.GPUController_PoweredOff;
            }
            else
            {
                _state = GPUState.NvidiaGpuNotFound;
            }

            Refreshed?.Invoke(this, new GPUStatus(_state, _performanceState, _processes, _deviceName));
        }
        finally
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Unloading NVAPI...");

            try
            {
                NVAPI.Unload();

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Unloaded NVAPI");
            }
            catch (Exception ex)
            {
                // Ignore unload errors
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"NVAPI unload error (ignored)", ex);
            }
        }
    }

    private async Task RefreshStateAsync()
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Refresh in progress...");

        _state = GPUState.Unknown;
        _processes = [];
        _gpuInstanceId = null;
        _performanceState = null;

        var gpu = NVAPI.GetGPU();
        if (gpu is null)
        {
            _state = GPUState.NvidiaGpuNotFound;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU present [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}]");

            return;
        }

        // Get GPU device name for model-specific optimizations (RTX 4060, RTX 4070, etc.)
        try
        {
            _deviceName = gpu.FullName;
        }
        catch
        {
            _deviceName = null;
        }

        try
        {
            var stateId = gpu.PerformanceStatesInfo.CurrentPerformanceState.StateId.ToString().GetUntilOrEmpty("_");
            _performanceState = Resource.GPUController_PoweredOn;
            if (!string.IsNullOrWhiteSpace(stateId))
                _performanceState += $", {stateId}";
        }
        catch (Exception ex) when (ex.Message == "NVAPI_GPU_NOT_POWERED")
        {
            _state = GPUState.PoweredOff;
            _performanceState = Resource.GPUController_PoweredOff;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Powered off [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}]");

            return;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU status exception.", ex);

            _performanceState = "Unknown";
        }

        var pnpDeviceIdPart = NVAPI.GetGPUId(gpu);

        if (string.IsNullOrEmpty(pnpDeviceIdPart))
            throw new InvalidOperationException("pnpDeviceIdPart is null or empty");

        var gpuInstanceId = await WMI.Win32.PnpEntity.GetDeviceIDAsync(pnpDeviceIdPart).ConfigureAwait(false);
        var processNames = NVAPIExtensions.GetActiveProcesses(gpu);

        if (NVAPI.IsDisplayConnected(gpu))
        {
            _processes = processNames;
            _state = GPUState.MonitorConnected;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace(
                    $"Monitor connected [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}]");
        }
        else if (processNames.Count != 0)
        {
            _processes = processNames;
            _state = GPUState.Active;
            _gpuInstanceId = gpuInstanceId;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Active [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}, pnpDeviceIdPart={pnpDeviceIdPart}]");
        }
        else
        {
            _state = GPUState.Inactive;
            _gpuInstanceId = gpuInstanceId;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Inactive [state={_state}, processes.Count={_processes.Count}, gpuInstanceId={_gpuInstanceId}]");
        }
    }

    // ELITE FIX v6.20.18: Proper disposal to prevent memory leaks
    public void Dispose()
    {
        try
        {
            // Stop refresh task
            if (_refreshCancellationTokenSource != null)
            {
                _refreshCancellationTokenSource.Cancel();
                _refreshCancellationTokenSource.Dispose();
                _refreshCancellationTokenSource = null;
            }

            // Wait for refresh task with timeout
            if (_refreshTask != null && !_refreshTask.IsCompleted)
            {
                var waitTask = _refreshTask.ContinueWith(_ => { }, TaskScheduler.Default);
                var completed = waitTask.Wait(TimeSpan.FromSeconds(2));

                if (!completed && Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"GPUController: Refresh task did not complete within timeout");
            }

            _refreshTask = null;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPUController: Disposal error", ex);
        }

        // CRITICAL: Clear event handlers to prevent memory leaks
        Refreshed = null;
    }
}
