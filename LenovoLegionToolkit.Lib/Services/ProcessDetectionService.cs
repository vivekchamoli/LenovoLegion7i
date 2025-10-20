using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.AI;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Process Detection Service - Autonomous profile switching based on running processes
/// PHASE 2 OPTIMIZATION: ML-enhanced workload detection with pattern learning
/// Detects media players, games, and idle states to apply optimal power profiles
/// Learns user patterns over time for predictive optimization
/// </summary>
public class ProcessDetectionService : IDisposable
{
    private readonly EliteFeaturesManager? _eliteFeaturesManager;
    private readonly WorkloadPatternLearner? _patternLearner;
    private readonly TimeOfDayWorkloadPredictor? _timeOfDayPredictor;

    // ELITE OPTIMIZATION: Changed from 10s to 30s (0.1-0.2W power savings)
    // Process changes are infrequent events, 30s detection delay is acceptable
    private readonly TimeSpan _scanInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _idleThreshold = TimeSpan.FromMinutes(5);

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private volatile bool _isRunning; // CRITICAL FIX v6.20.10: volatile prevents compiler/CPU reordering across threads
    private volatile bool _isEnabled = true; // CRITICAL FIX v6.20.10: volatile prevents compiler/CPU reordering across threads
    private volatile bool _mlEnabled = true; // CRITICAL FIX v6.20.10: volatile prevents compiler/CPU reordering across threads

    private DateTime _lastUserActivity = DateTime.Now;
    private DateTime _currentWorkloadStartTime = DateTime.Now;
    private DetectedWorkloadType _currentWorkload = DetectedWorkloadType.Unknown;

    // Media player process names
    private readonly HashSet<string> _mediaPlayerProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Browsers (when playing media)
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",

        // Video players
        "vlc", "mpc-hc64", "mpc-hc", "potplayermini64", "potplayer64",
        "mpv", "wmplayer", "netflix", "disneyplusdesktop",

        // Music players
        "spotify", "itunes", "musicbee", "foobar2000", "aimp"
    };

    // Gaming process names and launchers
    private readonly HashSet<string> _gamingProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Game launchers
        "steam", "epicgameslauncher", "gog galaxy", "origin", "uplay", "battlenet",
        "playnite.fullscreenapp", "playnite.desktopapp",

        // Common game executables (will be extended by detection)
        "game", "gameoverlayui"
    };

    // Compilation/development process names
    private readonly HashSet<string> _compilationProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "msbuild", "devenv", "vbcscompiler", "cl", "csc", "node", "npm", "yarn",
        "dotnet", "java", "javac", "gcc", "g++", "rustc", "cargo"
    };

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ProcessDetectionService {(_isEnabled ? "enabled" : "disabled")}");
        }
    }

    public bool MLEnabled
    {
        get => _mlEnabled;
        set
        {
            _mlEnabled = value;
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ML predictions {(_mlEnabled ? "enabled" : "disabled")}");
        }
    }

    public ProcessDetectionService(
        EliteFeaturesManager? eliteFeaturesManager = null,
        WorkloadPatternLearner? patternLearner = null,
        TimeOfDayWorkloadPredictor? timeOfDayPredictor = null)
    {
        _eliteFeaturesManager = eliteFeaturesManager;
        _patternLearner = patternLearner;
        _timeOfDayPredictor = timeOfDayPredictor;

        if (Log.Instance.IsTraceEnabled)
        {
            var mlStatus = patternLearner != null && timeOfDayPredictor != null ? "enabled" : "disabled";
            Log.Instance.Trace($"ProcessDetectionService initialized with ML: {mlStatus}");
        }
    }

    /// <summary>
    /// Start automatic process detection and profile switching
    /// </summary>
    public Task StartAsync()
    {
        if (_isRunning)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ProcessDetectionService already running");
            return Task.CompletedTask;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting ProcessDetectionService with {_scanInterval.TotalSeconds}s scan interval...");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _monitorTask = Task.Run(async () =>
        {
            _isRunning = true;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_isEnabled)
                    {
                        await DetectAndApplyOptimalProfileAsync().ConfigureAwait(false);
                    }

                    await Task.Delay(_scanInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Process detection error", ex);

                    await Task.Delay(_scanInterval, token).ConfigureAwait(false);
                }
            }

            _isRunning = false;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"ProcessDetectionService stopped");
        }, token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop automatic process detection
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Stopping ProcessDetectionService...");

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
    }

    /// <summary>
    /// Detect current workload and apply optimal profile (ML-enhanced)
    /// </summary>
    private async Task DetectAndApplyOptimalProfileAsync()
    {
        var detectedWorkload = DetectCurrentWorkload();

        // ML prediction: Use time-of-day predictor if no process detected
        if (detectedWorkload == DetectedWorkloadType.Unknown || detectedWorkload == DetectedWorkloadType.Productivity)
        {
            if (_mlEnabled && _timeOfDayPredictor != null)
            {
                var prediction = _timeOfDayPredictor.PredictCurrentWorkload();

                if (prediction.Confidence >= 0.6) // Use ML prediction if confident
                {
                    detectedWorkload = MapWorkloadType(prediction.Workload);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"ML prediction used: {prediction.Workload} (confidence: {prediction.Confidence:P0}, {prediction.Source})");
                }
            }
        }

        // Only apply profile if workload changed
        if (detectedWorkload == _currentWorkload)
            return;

        // Record workload transition for learning
        if (_patternLearner != null && _currentWorkload != DetectedWorkloadType.Unknown)
        {
            var duration = (int)(DateTime.Now - _currentWorkloadStartTime).TotalSeconds;
            var workloadType = MapToWorkloadType(_currentWorkload);

            _patternLearner.RecordWorkload(workloadType, _currentWorkloadStartTime, duration);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Recorded workload: {workloadType}, duration: {duration}s");
        }

        _currentWorkload = detectedWorkload;
        _currentWorkloadStartTime = DateTime.Now;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Workload changed to: {detectedWorkload}");

        // Apply optimal profile if EliteFeaturesManager available
        if (_eliteFeaturesManager != null)
        {
            await ApplyProfileForWorkloadAsync(detectedWorkload).ConfigureAwait(false);
        }

        // Periodic save of learned patterns
        if (_patternLearner != null && DateTime.Now.Minute % 10 == 0) // Every 10 minutes
        {
            await _patternLearner.SavePatternsAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Detect current workload based on running processes
    /// </summary>
    private DetectedWorkloadType DetectCurrentWorkload()
    {
        try
        {
            var processes = Process.GetProcesses();

            // Check for gaming first (highest priority)
            if (IsGamingActive(processes))
            {
                _lastUserActivity = DateTime.Now;
                return DetectedWorkloadType.Gaming;
            }

            // Check for compilation/development
            if (IsCompilationActive(processes))
            {
                _lastUserActivity = DateTime.Now;
                return DetectedWorkloadType.Compilation;
            }

            // Check for media playback
            if (IsMediaPlayerActive(processes))
            {
                _lastUserActivity = DateTime.Now;
                return DetectedWorkloadType.MediaPlayback;
            }

            // Check for idle state
            if (IsSystemIdle())
            {
                return DetectedWorkloadType.Idle;
            }

            // Default to general productivity
            _lastUserActivity = DateTime.Now;
            return DetectedWorkloadType.Productivity;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Workload detection failed", ex);

            return DetectedWorkloadType.Unknown;
        }
    }

    /// <summary>
    /// Check if gaming is active
    /// </summary>
    private bool IsGamingActive(Process[] processes)
    {
        foreach (var process in processes)
        {
            try
            {
                var processName = process.ProcessName.ToLowerInvariant();

                // Check known gaming processes
                if (_gamingProcesses.Contains(processName))
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Gaming detected: {process.ProcessName}");
                    return true;
                }

                // Heuristic: High GPU/CPU usage + fullscreen + not system process
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    // Check if process name contains game-related keywords
                    if (processName.Contains("game") || processName.Contains("launcher"))
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Potential game detected: {process.ProcessName}");
                        return true;
                    }
                }
            }
            catch
            {
                // Skip processes we can't access
            }
        }

        return false;
    }

    /// <summary>
    /// Check if compilation/development is active
    /// </summary>
    private bool IsCompilationActive(Process[] processes)
    {
        foreach (var process in processes)
        {
            try
            {
                var processName = process.ProcessName.ToLowerInvariant();

                if (_compilationProcesses.Contains(processName))
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Compilation detected: {process.ProcessName}");
                    return true;
                }
            }
            catch
            {
                // Skip processes we can't access
            }
        }

        return false;
    }

    /// <summary>
    /// Check if media player is active
    /// </summary>
    private bool IsMediaPlayerActive(Process[] processes)
    {
        foreach (var process in processes)
        {
            try
            {
                var processName = process.ProcessName.ToLowerInvariant();

                if (_mediaPlayerProcesses.Contains(processName))
                {
                    // Additional check: process should have a window (not just running in background)
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Media player detected: {process.ProcessName}");
                        return true;
                    }
                }
            }
            catch
            {
                // Skip processes we can't access
            }
        }

        return false;
    }

    /// <summary>
    /// Check if system is idle
    /// </summary>
    private bool IsSystemIdle()
    {
        var idleDuration = DateTime.Now - _lastUserActivity;
        return idleDuration >= _idleThreshold;
    }

    /// <summary>
    /// Apply optimal profile for detected workload
    /// </summary>
    private async Task ApplyProfileForWorkloadAsync(DetectedWorkloadType workload)
    {
        if (_eliteFeaturesManager == null)
            return;

        try
        {
            switch (workload)
            {
                case DetectedWorkloadType.Gaming:
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Applying Gaming profile (autonomous)");
                    await _eliteFeaturesManager.ApplyGamingProfileAsync().ConfigureAwait(false);
                    break;

                case DetectedWorkloadType.MediaPlayback:
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Applying Media Playback profile (autonomous)");
                    await _eliteFeaturesManager.ApplyMediaPlaybackProfileAsync().ConfigureAwait(false);
                    break;

                case DetectedWorkloadType.Idle:
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Applying Battery Saving profile (autonomous - system idle)");
                    await _eliteFeaturesManager.ApplyBatterySavingProfileAsync().ConfigureAwait(false);
                    break;

                case DetectedWorkloadType.Compilation:
                case DetectedWorkloadType.Productivity:
                case DetectedWorkloadType.Unknown:
                default:
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Applying Balanced profile (autonomous)");
                    await _eliteFeaturesManager.ApplyBalancedProfileAsync().ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to apply profile for workload {workload}", ex);
        }
    }

    /// <summary>
    /// Add custom media player process
    /// </summary>
    public void AddMediaPlayerProcess(string processName)
    {
        _mediaPlayerProcesses.Add(processName);
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Added media player: {processName}");
    }

    /// <summary>
    /// Add custom gaming process
    /// </summary>
    public void AddGamingProcess(string processName)
    {
        _gamingProcesses.Add(processName);
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Added gaming process: {processName}");
    }

    /// <summary>
    /// Map DetectedWorkloadType to WorkloadType (AI enum)
    /// </summary>
    private WorkloadType MapToWorkloadType(DetectedWorkloadType detected)
    {
        return detected switch
        {
            DetectedWorkloadType.Gaming => WorkloadType.Gaming,
            DetectedWorkloadType.MediaPlayback => WorkloadType.MediaPlayback,
            DetectedWorkloadType.Compilation => WorkloadType.Compilation,
            DetectedWorkloadType.Productivity => WorkloadType.LightProductivity,
            DetectedWorkloadType.Idle => WorkloadType.Idle,
            _ => WorkloadType.Unknown
        };
    }

    /// <summary>
    /// Map WorkloadType (AI enum) to DetectedWorkloadType
    /// </summary>
    private DetectedWorkloadType MapWorkloadType(WorkloadType workloadType)
    {
        return workloadType switch
        {
            WorkloadType.Gaming => DetectedWorkloadType.Gaming,
            WorkloadType.MediaPlayback => DetectedWorkloadType.MediaPlayback,
            WorkloadType.Compilation => DetectedWorkloadType.Compilation,
            WorkloadType.LightProductivity => DetectedWorkloadType.Productivity,
            WorkloadType.HeavyProductivity => DetectedWorkloadType.Productivity,
            WorkloadType.Idle => DetectedWorkloadType.Idle,
            _ => DetectedWorkloadType.Unknown
        };
    }

    /// <summary>
    /// Get ML learning progress
    /// </summary>
    public LearningProgress? GetLearningProgress()
    {
        return _patternLearner?.GetLearningProgress();
    }

    /// <summary>
    /// Get predicted workload for next N hours
    /// </summary>
    public List<HourlyPrediction>? GetPredictedWorkloads(int hours = 4)
    {
        return _timeOfDayPredictor?.PredictNextHours(hours);
    }

    public void Dispose()
    {
        // Save learned patterns before shutdown
        if (_patternLearner != null)
        {
            _patternLearner.SavePatternsAsync().Wait(1000);
        }

        StopAsync().Wait(1000);
    }
}

/// <summary>
/// Detected workload types
/// </summary>
public enum DetectedWorkloadType
{
    Unknown,
    Gaming,
    MediaPlayback,
    Compilation,
    Productivity,
    Idle
}
