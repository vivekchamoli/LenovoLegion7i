using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI;

/// <summary>
/// GPU Agent - Intelligent graphics power management and process prioritization
/// Handles dynamic GPU power states, overclocking, and process scheduling
/// </summary>
public class GPUAgent : IOptimizationAgent
{
    private readonly GPUController _gpuController;
    private readonly GPUOverclockController? _overclockController;

    // Critical processes that should never be deprioritized
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "explorer", "csrss", "winlogon", "system", "smss",
        "lsass", "services", "svchost", "audiodg"
    };

    // Background processes that can be deprioritized
    private static readonly HashSet<string> BackgroundProcessPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "discord", "slack", "teams",
        "spotify", "obs", "streamlabs", "nvidia", "geforce"
    };

    public string AgentName => "GPUAgent";
    public AgentPriority Priority => AgentPriority.Medium;

    public GPUAgent(GPUController gpuController, GPUOverclockController? overclockController)
    {
        _gpuController = gpuController ?? throw new ArgumentNullException(nameof(gpuController));
        _overclockController = overclockController;
    }

    public async Task<AgentProposal> ProposeActionsAsync(SystemContext context)
    {
        var proposal = new AgentProposal
        {
            Agent = AgentName,
            Priority = Priority
        };

        if (!_gpuController.IsSupported())
            return proposal;

        // Analyze GPU state and workload
        var gpuState = context.GpuState;

        // GAMING WORKLOAD - Maximize GPU performance
        if (context.CurrentWorkload.Type == WorkloadType.Gaming)
        {
            await HandleGamingWorkloadAsync(proposal, context).ConfigureAwait(false);
        }
        // AI/ML WORKLOAD - Maximize compute performance
        else if (context.CurrentWorkload.Type == WorkloadType.AIWorkload)
        {
            await HandleAIWorkloadAsync(proposal, context).ConfigureAwait(false);
        }
        // GPU ACTIVE BUT IDLE - Optimize or power down
        else if (gpuState.State == GPUState.Active && gpuState.GpuUtilizationPercent < 10)
        {
            await HandleIdleGPUAsync(proposal, context).ConfigureAwait(false);
        }
        // GPU COMPLETELY IDLE - Aggressive power management
        else if (gpuState.State == GPUState.Inactive)
        {
            await HandleInactiveGPUAsync(proposal, context).ConfigureAwait(false);
        }

        return proposal;
    }

    public Task OnActionsExecutedAsync(ExecutionResult result)
    {
        // Learn from GPU optimization outcomes
        if (result.Success && result.ExecutedActions.Any(a => a.Target.Contains("GPU")))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"GPU actions executed successfully");

            // TODO: Collect GPU performance metrics for learning
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Optimize for gaming workload
    /// </summary>
    private Task HandleGamingWorkloadAsync(AgentProposal proposal, SystemContext context)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"GPU Agent: Gaming workload detected");

        // Maximum GPU power
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "GPU_TGP",
            Value = 140, // Max TGP for RTX 4070
            Reason = "Gaming workload - maximum GPU performance"
        });

        // Apply overclock profile if thermal headroom available
        if (_overclockController != null &&
            context.ThermalState.GpuTemp < 75 &&
            context.PowerState.IsACConnected)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "GPU_OVERCLOCK",
                Value = new GPUOverclockProfile
                {
                    Name = "Gaming Performance",
                    CoreClockOffset = 150,      // +150MHz
                    MemoryClockOffset = 500,    // +500MHz
                    PowerLimit = 140,
                    TempLimit = 87
                },
                Reason = $"Gaming + thermal headroom ({85 - context.ThermalState.GpuTemp}°C)"
            });
        }

        // Deprioritize background GPU processes
        var backgroundProcesses = context.GpuState.ActiveProcesses
            .Where(p => IsBackgroundProcess(p) && !IsCriticalProcess(p))
            .Select(p => p.Id)
            .ToList();

        if (backgroundProcesses.Count > 0)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "GPU_PROCESS_PRIORITY",
                Value = ProcessPriorityClass.BelowNormal,
                AffectedProcesses = backgroundProcesses,
                Reason = $"Deprioritize {backgroundProcesses.Count} background GPU processes for gaming"
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Optimize for AI/ML workload
    /// </summary>
    private Task HandleAIWorkloadAsync(AgentProposal proposal, SystemContext context)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"GPU Agent: AI/ML workload detected");

        // Maximum compute performance
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Proactive,
            Target = "GPU_TGP",
            Value = 140,
            Reason = "AI/ML workload - maximum compute performance"
        });

        // Prefer compute over graphics
        if (_overclockController != null && context.PowerState.IsACConnected)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Proactive,
                Target = "GPU_OVERCLOCK",
                Value = new GPUOverclockProfile
                {
                    Name = "Compute Optimized",
                    CoreClockOffset = 100,      // Conservative core
                    MemoryClockOffset = 800,    // Aggressive memory for AI
                    PowerLimit = 140,
                    TempLimit = 87
                },
                Reason = "AI/ML workload - memory bandwidth priority"
            });
        }

        // Reduce CPU PL2 slightly to give more power budget to GPU
        if (context.ThermalState.CpuTemp > 80)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "CPU_PL2",
                Value = Math.Max(90, context.PowerState.CurrentPL2 - 20),
                Reason = "Shift power budget to GPU for AI workload"
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle GPU that's active but underutilized
    /// </summary>
    private Task HandleIdleGPUAsync(AgentProposal proposal, SystemContext context)
    {
        var processes = context.GpuState.ActiveProcesses;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"GPU Agent: GPU active but idle ({processes.Count} processes)");

        // Identify background processes using GPU unnecessarily
        var backgroundProcesses = processes
            .Where(p => IsBackgroundProcess(p) && !IsCriticalProcess(p))
            .Select(p => p.Id)
            .ToList();

        if (backgroundProcesses.Count > 0)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "GPU_PROCESS_PRIORITY",
                Value = ProcessPriorityClass.Idle,
                AffectedProcesses = backgroundProcesses,
                Reason = $"GPU idle - deprioritize {backgroundProcesses.Count} background processes"
            });

            // Suggest killing processes if on battery
            if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 30)
            {
                proposal.Actions.Add(new ResourceAction
                {
                    Type = ActionType.Opportunistic,
                    Target = "GPU_PROCESS_TERMINATE",
                    Value = true,
                    AffectedProcesses = backgroundProcesses,
                    Reason = "Battery critical - terminate background GPU processes",
                    Parameters = new Dictionary<string, object>
                    {
                        ["ProcessNames"] = processes
                            .Where(p => backgroundProcesses.Contains(p.Id))
                            .Select(p => p.ProcessName)
                            .ToList()
                    }
                });
            }
        }

        // Reduce GPU power if idle for extended period
        if (context.GpuState.GpuUtilizationPercent < 5)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "GPU_TGP",
                Value = 80,
                Reason = "GPU underutilized - reduce power consumption"
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle completely inactive GPU
    /// </summary>
    private Task HandleInactiveGPUAsync(AgentProposal proposal, SystemContext context)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"GPU Agent: GPU completely inactive");

        // Aggressive power gating on battery
        if (context.BatteryState.IsOnBattery && context.BatteryState.ChargePercent < 50)
        {
            proposal.Actions.Add(new ResourceAction
            {
                Type = ActionType.Opportunistic,
                Target = "GPU_POWER_STATE",
                Value = "D3Cold", // Deep sleep state
                Reason = $"GPU idle + battery saving ({context.BatteryState.ChargePercent}%)"
            });
        }

        // Minimum GPU power even on AC (for responsiveness)
        proposal.Actions.Add(new ResourceAction
        {
            Type = ActionType.Opportunistic,
            Target = "GPU_TGP",
            Value = 60,
            Reason = "GPU inactive - minimum power mode"
        });

        return Task.CompletedTask;
    }

    private bool IsCriticalProcess(Process process)
    {
        try
        {
            return CriticalProcesses.Contains(process.ProcessName);
        }
        catch
        {
            return true; // If we can't determine, assume critical for safety
        }
    }

    private bool IsBackgroundProcess(Process process)
    {
        try
        {
            return BackgroundProcessPatterns.Any(pattern =>
                process.ProcessName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// GPU overclock profile
/// </summary>
public class GPUOverclockProfile
{
    public string Name { get; set; } = "Default";
    public int CoreClockOffset { get; set; }    // MHz offset
    public int MemoryClockOffset { get; set; }  // MHz offset
    public int PowerLimit { get; set; }         // Watts
    public int TempLimit { get; set; }          // Celsius
}
