using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Process Launch Monitor - Predictive GPU switching based on app launches
/// Detects process starts and predicts GPU requirements BEFORE workload classification
/// Phase 2: Predictive Intelligence from Elite GPU/Power Analysis
/// </summary>
public class ProcessLaunchMonitor : IDisposable
{
    private ManagementEventWatcher? _processStartWatcher;
    private readonly Dictionary<string, GPURequirement> _knownProcesses = new();
    private readonly object _lockObj = new();
    private bool _isRunning = false;

    // Events
    public event EventHandler<ProcessLaunchPrediction>? ProcessLaunched;

    public ProcessLaunchMonitor()
    {
        InitializeKnownProcesses();
    }

    /// <summary>
    /// Initialize database of known GPU-intensive processes
    /// </summary>
    private void InitializeKnownProcesses()
    {
        lock (_lockObj)
        {
            // Gaming / 3D Applications (dGPU required)
            var gpuRequired = new[]
            {
                "cyberpunk2077.exe", "witcher3.exe", "eldenring.exe", "valorant.exe",
                "league of legends.exe", "dota2.exe", "csgo.exe", "overwatch.exe",
                "apex_legends.exe", "fortnite.exe", "pubg.exe", "warzone.exe",
                "gta5.exe", "rdr2.exe", "minecraft.exe", "roblox.exe",
                "unityplayer.exe", "unrealengine.exe", "blender.exe", "maya.exe",
                "3dsmax.exe", "cinema4d.exe", "substancepainter.exe"
            };

            // Video Editing (prefer dGPU but can use iGPU)
            var gpuPreferred = new[]
            {
                "premiere.exe", "aftereffects.exe", "davinciresolve.exe",
                "vegaspro.exe", "filmora.exe", "camtasia.exe", "obs64.exe",
                "streamlabs obs.exe", "xsplit.broadcaster.exe"
            };

            // Media Playback (iGPU optimal - Intel QuickSync)
            var igpuOptimal = new[]
            {
                "vlc.exe", "mpc-hc64.exe", "mpc-be64.exe", "potplayermini64.exe",
                "wmplayer.exe", "netflix.exe", "spotify.exe", "itunes.exe",
                "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe",
                "discord.exe", "slack.exe", "teams.exe", "zoom.exe",
                "skype.exe", "whatsapp.exe"
            };

            // Productivity (iGPU sufficient)
            var igpuSufficient = new[]
            {
                "winword.exe", "excel.exe", "powerpnt.exe", "outlook.exe",
                "onenote.exe", "notepad++.exe", "code.exe", "devenv.exe",
                "rider64.exe", "idea64.exe", "pycharm64.exe", "webstorm64.exe",
                "explorer.exe", "taskmgr.exe", "cmd.exe", "powershell.exe"
            };

            foreach (var proc in gpuRequired)
                _knownProcesses[proc.ToLowerInvariant()] = GPURequirement.Required;

            foreach (var proc in gpuPreferred)
                _knownProcesses[proc.ToLowerInvariant()] = GPURequirement.Preferred;

            foreach (var proc in igpuOptimal)
                _knownProcesses[proc.ToLowerInvariant()] = GPURequirement.IGPUOptimal;

            foreach (var proc in igpuSufficient)
                _knownProcesses[proc.ToLowerInvariant()] = GPURequirement.IGPUSufficient;
        }
    }

    /// <summary>
    /// Start monitoring process launches
    /// </summary>
    public Task StartAsync()
    {
        if (_isRunning)
            return Task.CompletedTask;

        try
        {
            // WMI query for process creation events
            var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
            _processStartWatcher = new ManagementEventWatcher(query);
            _processStartWatcher.EventArrived += OnProcessStarted;
            _processStartWatcher.Start();

            _isRunning = true;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Process launch monitor started - tracking {_knownProcesses.Count} known processes");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to start process launch monitor", ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop monitoring
    /// </summary>
    public Task StopAsync()
    {
        if (!_isRunning)
            return Task.CompletedTask;

        try
        {
            _processStartWatcher?.Stop();
            _processStartWatcher?.Dispose();
            _processStartWatcher = null;
            _isRunning = false;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Process launch monitor stopped");
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Error stopping process launch monitor", ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle process start event
    /// </summary>
    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processName = e.NewEvent.Properties["ProcessName"].Value?.ToString();
            if (string.IsNullOrEmpty(processName))
                return;

            var processNameLower = processName.ToLowerInvariant();

            // Check if this is a known GPU-sensitive process
            GPURequirement requirement;
            lock (_lockObj)
            {
                if (!_knownProcesses.TryGetValue(processNameLower, out requirement))
                    return; // Unknown process, no prediction
            }

            // Get process ID for additional context
            var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);

            // Create prediction
            var prediction = new ProcessLaunchPrediction
            {
                ProcessName = processName,
                ProcessId = processId,
                Requirement = requirement,
                RecommendedMode = GetRecommendedMode(requirement),
                Confidence = GetConfidence(processNameLower, requirement),
                Timestamp = DateTime.Now
            };

            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"Process launch detected: {processName} (PID: {processId}) → GPU Requirement: {requirement}, Recommended: {prediction.RecommendedMode}, Confidence: {prediction.Confidence}%");
            }

            // Notify subscribers (HybridModeAgent will receive this)
            ProcessLaunched?.Invoke(this, prediction);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Error processing process start event", ex);
        }
    }

    /// <summary>
    /// Get recommended GPU mode based on requirement
    /// </summary>
    private HybridModeState GetRecommendedMode(GPURequirement requirement)
    {
        return requirement switch
        {
            GPURequirement.Required => HybridModeState.Off, // dGPU always on
            GPURequirement.Preferred => HybridModeState.On, // Hybrid mode (auto-switch)
            GPURequirement.IGPUOptimal => HybridModeState.OnIGPUOnly, // iGPU only (QuickSync)
            GPURequirement.IGPUSufficient => HybridModeState.OnIGPUOnly, // iGPU sufficient
            _ => HybridModeState.OnAuto // Unknown, let OS decide
        };
    }

    /// <summary>
    /// Get confidence level for prediction
    /// </summary>
    private int GetConfidence(string processNameLower, GPURequirement requirement)
    {
        // High confidence for well-known GPU-intensive apps
        if (requirement == GPURequirement.Required &&
            (processNameLower.Contains("game") || processNameLower.Contains("unreal") ||
             processNameLower.Contains("unity") || processNameLower.Contains("blender")))
        {
            return 95;
        }

        // High confidence for media players (iGPU optimal)
        if (requirement == GPURequirement.IGPUOptimal &&
            (processNameLower.Contains("vlc") || processNameLower.Contains("mpc") ||
             processNameLower.Contains("netflix") || processNameLower.Contains("spotify")))
        {
            return 90;
        }

        // Medium-high for browsers (iGPU sufficient)
        if (processNameLower.Contains("chrome") || processNameLower.Contains("edge") ||
            processNameLower.Contains("firefox"))
        {
            return 80;
        }

        // Default confidence
        return 70;
    }

    /// <summary>
    /// Check if a specific process is GPU-intensive
    /// </summary>
    public bool IsGPUIntensive(string processName)
    {
        if (string.IsNullOrEmpty(processName))
            return false;

        lock (_lockObj)
        {
            var key = processName.ToLowerInvariant();
            if (_knownProcesses.TryGetValue(key, out var requirement))
            {
                return requirement == GPURequirement.Required ||
                       requirement == GPURequirement.Preferred;
            }
        }

        return false;
    }

    /// <summary>
    /// Get statistics
    /// </summary>
    public ProcessLaunchMonitorStats GetStatistics()
    {
        lock (_lockObj)
        {
            return new ProcessLaunchMonitorStats
            {
                IsRunning = _isRunning,
                KnownProcessCount = _knownProcesses.Count,
                GPURequiredCount = _knownProcesses.Count(kvp => kvp.Value == GPURequirement.Required),
                IGPUOptimalCount = _knownProcesses.Count(kvp => kvp.Value == GPURequirement.IGPUOptimal)
            };
        }
    }

    public void Dispose()
    {
        StopAsync().Wait();
    }
}

/// <summary>
/// GPU Requirement Classification
/// </summary>
public enum GPURequirement
{
    /// <summary>dGPU required (gaming, 3D, rendering)</summary>
    Required,

    /// <summary>dGPU preferred but iGPU can work (video editing)</summary>
    Preferred,

    /// <summary>iGPU optimal (media playback - QuickSync advantage)</summary>
    IGPUOptimal,

    /// <summary>iGPU sufficient (productivity, web browsing)</summary>
    IGPUSufficient,

    /// <summary>Unknown process</summary>
    Unknown
}

/// <summary>
/// Process Launch Prediction
/// </summary>
public class ProcessLaunchPrediction
{
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public GPURequirement Requirement { get; set; }
    public HybridModeState RecommendedMode { get; set; }
    public int Confidence { get; set; } // 0-100%
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Monitor Statistics
/// </summary>
public class ProcessLaunchMonitorStats
{
    public bool IsRunning { get; set; }
    public int KnownProcessCount { get; set; }
    public int GPURequiredCount { get; set; }
    public int IGPUOptimalCount { get; set; }
}
