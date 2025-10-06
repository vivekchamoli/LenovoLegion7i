using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.AutoListeners;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// ML-based workload classification for intelligent power management
/// Classifies current system activity into workload types
/// </summary>
public class WorkloadClassifier
{
    private readonly GameAutoListener _gameAutoListener;
    private WorkloadProfile? _lastWorkload;
    private DateTime _lastWorkloadChangeTime = DateTime.UtcNow;

    // Known gaming process patterns
    private static readonly HashSet<string> GamingProcessPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "steam", "epic", "origin", "uplay", "gog", "battle.net",
        "launcher", "minecraft", "fortnite", "valorant", "league",
        "dota", "counter-strike", "cs2", "warzone", "apex", "overwatch",
        "fifa", "elden", "cyberpunk", "gta", "witcher", "starfield"
    };

    // Known media playback process patterns (CRITICAL for movie watching optimization)
    private static readonly HashSet<string> MediaPlaybackPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc", "mpc-hc", "mpc-be", "potplayer", "kmplayer", "mpv",
        "netflix", "disney", "prime", "hulu", "plex", "kodi",
        "youtube", "twitch", "spotify", "foobar", "aimp", "musicbee",
        "audirvana", "tidal", "deezer", "pandora"
    };

    // Known video conferencing patterns (Zoom, Teams, Discord)
    private static readonly HashSet<string> VideoConferencingPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "zoom", "teams", "discord", "skype", "webex", "gotomeeting",
        "slack", "meet", "hangouts", "whereby", "jitsi"
    };

    // Known productivity process patterns
    private static readonly HashSet<string> ProductivityPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "studio", "intellij", "pycharm", "eclipse", "netbeans",
        "word", "excel", "powerpoint", "outlook", "teams", "slack",
        "chrome", "firefox", "edge", "notion", "obsidian", "evernote",
        "onenote", "acrobat", "reader", "notepad"
    };

    // Known content creation patterns
    private static readonly HashSet<string> ContentCreationPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "photoshop", "premiere", "aftereffects", "illustrator", "lightroom",
        "blender", "maya", "3dsmax", "cinema4d", "houdini", "resolve",
        "vegas", "davinci", "final cut", "autocad", "solidworks", "fusion360",
        "substance", "zbrush", "marmoset", "unreal", "unity"
    };

    // Known AI/ML process patterns
    private static readonly HashSet<string> AIWorkloadPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "pytorch", "tensorflow", "cuda", "jupyter",
        "stable-diffusion", "comfyui", "automatic1111", "ollama",
        "conda", "anaconda", "spyder", "rstudio"
    };

    // Known compiler/build tool patterns (heavy CPU burst workloads)
    private static readonly HashSet<string> CompilerPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "cl.exe", "gcc", "g++", "clang", "rustc", "javac", "msbuild",
        "gradle", "maven", "npm", "yarn", "webpack", "cargo", "dotnet"
    };

    public WorkloadClassifier(GameAutoListener gameAutoListener)
    {
        _gameAutoListener = gameAutoListener;
    }

    /// <summary>
    /// Classify current workload based on system context
    /// Uses ML heuristics and process analysis
    /// </summary>
    public async Task<WorkloadProfile> ClassifyAsync(SystemContext context)
    {
        var profile = new WorkloadProfile
        {
            CpuUtilizationPercent = await GetCpuUtilizationAsync().ConfigureAwait(false),
            GpuUtilizationPercent = context.GpuState.GpuUtilizationPercent,
            ActiveApplications = GetActiveProcessNames(),
            IsUserActive = IsUserActive()
        };

        // Get gaming processes from GameAutoListener
        var gamesRunning = _gameAutoListener.AreGamesRunning();
        if (gamesRunning)
        {
            profile.GamingProcesses = GetRunningGames();
        }

        // Classify workload type
        (profile.Type, profile.Confidence) = ClassifyWorkloadType(profile, context);

        // Track time in current workload
        if (_lastWorkload != null && _lastWorkload.Type == profile.Type)
        {
            profile.TimeInCurrentWorkload = DateTime.UtcNow - _lastWorkloadChangeTime;
        }
        else
        {
            _lastWorkloadChangeTime = DateTime.UtcNow;
            profile.TimeInCurrentWorkload = TimeSpan.Zero;
        }

        _lastWorkload = profile;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Workload classified: {profile.Type} (confidence: {profile.Confidence:P0})");

        return profile;
    }

    private (WorkloadType type, double confidence) ClassifyWorkloadType(
        WorkloadProfile profile,
        SystemContext context)
    {
        var scores = new Dictionary<WorkloadType, double>();

        // Gaming classification (highest GPU priority)
        if (profile.GamingProcesses.Count > 0)
        {
            scores[WorkloadType.Gaming] = 0.95;
        }
        else if (context.GpuState.GpuUtilizationPercent > 70 &&
                 ContainsGamingProcesses(profile.ActiveApplications))
        {
            scores[WorkloadType.Gaming] = 0.85;
        }

        // Media playback classification (CRITICAL for movie watching)
        // Low CPU, very low GPU (hardware decode), specific processes
        if (ContainsMediaPlaybackProcesses(profile.ActiveApplications))
        {
            scores[WorkloadType.MediaPlayback] = 0.90;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Media playback detected - activating power-saving optimizations");
        }
        else if (profile.CpuUtilizationPercent < 30 &&
                 context.GpuState.GpuUtilizationPercent < 15 &&
                 profile.IsUserActive)
        {
            // Likely watching video in browser (YouTube, Netflix web)
            scores[WorkloadType.MediaPlayback] = 0.70;
        }

        // Video conferencing (camera + microphone + low-medium CPU)
        if (ContainsVideoConferencingProcesses(profile.ActiveApplications))
        {
            scores[WorkloadType.VideoConferencing] = 0.90;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Video conferencing detected - balanced optimization");
        }

        // AI/ML workload classification (high GPU memory + CUDA processes)
        if (ContainsAIProcesses(profile.ActiveApplications))
        {
            scores[WorkloadType.AIWorkload] = 0.90;
        }
        else if (context.GpuState.GpuUtilizationPercent > 80 &&
                 context.GpuState.MemoryUtilizationPercent > 60)
        {
            scores[WorkloadType.AIWorkload] = 0.70;
        }

        // Content creation (high CPU + high GPU + creation apps)
        if (profile.CpuUtilizationPercent > 60 &&
            context.GpuState.GpuUtilizationPercent > 40 &&
            ContainsContentCreationProcesses(profile.ActiveApplications))
        {
            scores[WorkloadType.ContentCreation] = 0.85;
        }

        // Compilation/build workload (CPU burst, specific compiler processes)
        if (ContainsCompilerProcesses(profile.ActiveApplications) &&
            profile.CpuUtilizationPercent > 70)
        {
            scores[WorkloadType.Compilation] = 0.88;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Compilation detected - CPU boost optimization");
        }

        // Heavy productivity (high CPU, low GPU)
        if (profile.CpuUtilizationPercent > 50 &&
            context.GpuState.GpuUtilizationPercent < 20 &&
            ContainsProductivityProcesses(profile.ActiveApplications))
        {
            scores[WorkloadType.HeavyProductivity] = 0.80;
        }

        // Light productivity
        if (profile.CpuUtilizationPercent > 10 &&
            profile.CpuUtilizationPercent < 50 &&
            profile.IsUserActive &&
            ContainsProductivityProcesses(profile.ActiveApplications))
        {
            scores[WorkloadType.LightProductivity] = 0.75;
        }

        // Idle classification
        if (profile.CpuUtilizationPercent < 10 &&
            context.GpuState.GpuUtilizationPercent < 10 &&
            !profile.IsUserActive)
        {
            scores[WorkloadType.Idle] = 0.90;
        }

        // Mixed workload (multiple high-confidence scores)
        if (scores.Count(s => s.Value > 0.7) > 1)
        {
            scores[WorkloadType.Mixed] = 0.75;
        }

        // Return highest confidence classification
        if (scores.Count > 0)
        {
            var best = scores.OrderByDescending(s => s.Value).First();
            return (best.Key, best.Value);
        }

        return (WorkloadType.Unknown, 0.5);
    }

    private async Task<int> GetCpuUtilizationAsync()
    {
        try
        {
            // Use PerformanceCounter for accurate CPU usage
            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue(); // First call returns 0
            await Task.Delay(100).ConfigureAwait(false);
            return (int)cpuCounter.NextValue();
        }
        catch
        {
            return 0;
        }
    }

    private List<string> GetActiveProcessNames()
    {
        try
        {
            return Process.GetProcesses()
                .Where(p => p.WorkingSet64 > 50_000_000) // > 50MB memory
                .Select(p => p.ProcessName)
                .Distinct()
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private List<string> GetRunningGames()
    {
        try
        {
            var processes = Process.GetProcesses();
            return processes
                .Where(p => ContainsPattern(p.ProcessName, GamingProcessPatterns))
                .Select(p => p.ProcessName)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private bool IsUserActive()
    {
        try
        {
            // Get time since last input (keyboard/mouse)
            var lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);

            if (GetLastInputInfo(ref lastInputInfo))
            {
                var idleTime = Environment.TickCount - lastInputInfo.dwTime;
                // Consider user active if input within last 5 minutes (300,000 ms)
                return idleTime < 300000;
            }

            // If unable to get input info, assume active
            return true;
        }
        catch
        {
            // On error, assume active
            return true;
        }
    }

    // Windows API for user activity detection
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private bool ContainsGamingProcesses(List<string> processes)
    {
        return processes.Any(p => ContainsPattern(p, GamingProcessPatterns));
    }

    private bool ContainsProductivityProcesses(List<string> processes)
    {
        return processes.Any(p => ContainsPattern(p, ProductivityPatterns));
    }

    private bool ContainsAIProcesses(List<string> processes)
    {
        return processes.Any(p => ContainsPattern(p, AIWorkloadPatterns));
    }

    private bool ContainsContentCreationProcesses(List<string> processes)
    {
        return processes.Any(p => ContainsPattern(p, ContentCreationPatterns));
    }

    private bool ContainsMediaPlaybackProcesses(List<string> processes)
    {
        return processes.Any(p => ContainsPattern(p, MediaPlaybackPatterns));
    }

    private bool ContainsVideoConferencingProcesses(List<string> processes)
    {
        return processes.Any(p => ContainsPattern(p, VideoConferencingPatterns));
    }

    private bool ContainsCompilerProcesses(List<string> processes)
    {
        return processes.Any(p => ContainsPattern(p, CompilerPatterns));
    }

    private bool ContainsPattern(string processName, HashSet<string> patterns)
    {
        return patterns.Any(pattern =>
            processName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
