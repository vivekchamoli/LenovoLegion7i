using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        "dota", "counter-strike", "cs2", "warzone", "apex", "overwatch"
    };

    // Known productivity process patterns
    private static readonly HashSet<string> ProductivityPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "studio", "intellij", "pycharm", "eclipse", "netbeans",
        "word", "excel", "powerpoint", "outlook", "teams", "slack",
        "chrome", "firefox", "edge", "photoshop", "premiere", "illustrator",
        "blender", "autocad", "solidworks"
    };

    // Known AI/ML process patterns
    private static readonly HashSet<string> AIWorkloadPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "pytorch", "tensorflow", "cuda", "jupyter",
        "stable-diffusion", "comfyui", "automatic1111", "ollama"
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

        // Gaming classification
        if (profile.GamingProcesses.Count > 0)
        {
            scores[WorkloadType.Gaming] = 0.95;
        }
        else if (context.GpuState.GpuUtilizationPercent > 70 &&
                 ContainsGamingProcesses(profile.ActiveApplications))
        {
            scores[WorkloadType.Gaming] = 0.85;
        }

        // AI/ML workload classification
        if (ContainsAIProcesses(profile.ActiveApplications))
        {
            scores[WorkloadType.AIWorkload] = 0.90;
        }
        else if (context.GpuState.GpuUtilizationPercent > 80 &&
                 context.GpuState.MemoryUtilizationPercent > 60)
        {
            scores[WorkloadType.AIWorkload] = 0.70;
        }

        // Content creation (high CPU + high GPU)
        if (profile.CpuUtilizationPercent > 60 &&
            context.GpuState.GpuUtilizationPercent > 40 &&
            ContainsContentCreationProcesses(profile.ActiveApplications))
        {
            scores[WorkloadType.ContentCreation] = 0.85;
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
        // TODO: Implement proper user activity detection
        // For now, assume active if not idle for long
        return true;
    }

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
        var contentCreationApps = new[] { "photoshop", "premiere", "aftereffects",
            "blender", "maya", "3dsmax", "resolve", "vegas" };
        return processes.Any(p => contentCreationApps.Any(app =>
            p.IndexOf(app, StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private bool ContainsPattern(string processName, HashSet<string> patterns)
    {
        return patterns.Any(pattern =>
            processName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
