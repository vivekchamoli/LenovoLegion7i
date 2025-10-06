using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Display Topology Service - Tracks which displays are connected to which GPU
/// Critical for preventing iGPU-only mode when external monitor is on dGPU
/// Phase 1 Critical Fix from Elite GPU/Power Analysis
/// </summary>
public class DisplayTopologyService
{
    private readonly GPUController _gpuController;
    private DateTime _lastTopologyCheck = DateTime.MinValue;
    private DisplayTopology _cachedTopology = new();

    // Cache for 30 seconds to reduce NVAPI calls
    private readonly TimeSpan _cacheValidity = TimeSpan.FromSeconds(30);

    public DisplayTopologyService(GPUController gpuController)
    {
        _gpuController = gpuController ?? throw new ArgumentNullException(nameof(gpuController));
    }

    /// <summary>
    /// Get current display topology with caching
    /// </summary>
    public Task<DisplayTopology> GetTopologyAsync()
    {
        var now = DateTime.Now;

        // Return cached if still valid
        if (now - _lastTopologyCheck < _cacheValidity)
            return Task.FromResult(_cachedTopology);

        // Refresh topology
        return RefreshTopologyAsync();
    }

    private async Task<DisplayTopology> RefreshTopologyAsync()
    {
        var topology = await DetectTopologyAsync().ConfigureAwait(false);
        _cachedTopology = topology;
        _lastTopologyCheck = DateTime.Now;
        return _cachedTopology;
    }

    /// <summary>
    /// Detect display topology from NVAPI
    /// </summary>
    private Task<DisplayTopology> DetectTopologyAsync()
    {
        var topology = new DisplayTopology();

        try
        {
            // Check if dGPU is available
            if (!_gpuController.IsSupported())
            {
                topology.IsNvidiaGPUAvailable = false;
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Display topology: No NVIDIA GPU detected");
                return Task.FromResult(topology);
            }

            topology.IsNvidiaGPUAvailable = true;

            // Check if dGPU has displays connected
            NVAPI.Initialize();
            try
            {
                var gpu = NVAPI.GetGPU();
                if (gpu != null)
                {
                    var hasDisplayConnected = NVAPI.IsDisplayConnected(gpu);
                    topology.HasExternalDisplayOnDGPU = hasDisplayConnected;

                    if (hasDisplayConnected)
                    {
                        // Assume 1 display if connected (GetDisplayIds not available)
                        topology.DGPUDisplayCount = 1;

                        if (Log.Instance.IsTraceEnabled)
                        {
                            Log.Instance.Trace($"Display topology: {topology.DGPUDisplayCount} display(s) on dGPU");
                        }
                    }
                    else if (Log.Instance.IsTraceEnabled)
                    {
                        Log.Instance.Trace($"Display topology: No displays on dGPU");
                    }
                }
            }
            finally
            {
                NVAPI.Unload();
            }

            // Estimate total display count (basic heuristic)
            // Internal display (1) + dGPU displays
            topology.TotalDisplayCount = 1 + topology.DGPUDisplayCount;

            // If dGPU has displays, we know there's at least one external
            topology.HasExternalDisplay = topology.DGPUDisplayCount > 0;
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to detect display topology", ex);

            // Safe fallback: assume no external display on dGPU
            topology.IsNvidiaGPUAvailable = false;
        }

        return Task.FromResult(topology);
    }

    /// <summary>
    /// Check if iGPU-only mode is safe (won't blank external displays)
    /// </summary>
    public async Task<bool> IsIGPUOnlyModeSafeAsync()
    {
        var topology = await GetTopologyAsync().ConfigureAwait(false);

        // Safe if no external display on dGPU
        var isSafe = !topology.HasExternalDisplayOnDGPU;

        if (!isSafe && Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"iGPU-only mode BLOCKED: External display connected to dGPU (would cause display to blank)");
        }

        return isSafe;
    }

    /// <summary>
    /// Get recommended GPU mode based on display topology
    /// </summary>
    public async Task<DisplayTopologyRecommendation> GetRecommendationAsync()
    {
        var topology = await GetTopologyAsync().ConfigureAwait(false);

        if (!topology.IsNvidiaGPUAvailable)
        {
            return new DisplayTopologyRecommendation
            {
                AllowIGPUOnly = true,
                Reason = "No NVIDIA GPU detected - iGPU-only safe"
            };
        }

        if (topology.HasExternalDisplayOnDGPU)
        {
            return new DisplayTopologyRecommendation
            {
                AllowIGPUOnly = false,
                BlockedReason = $"External display connected to dGPU (would blank on iGPU-only)",
                RequiresDGPU = true,
                Reason = $"{topology.DGPUDisplayCount} external display(s) require dGPU active"
            };
        }

        return new DisplayTopologyRecommendation
        {
            AllowIGPUOnly = true,
            Reason = "All displays on iGPU - iGPU-only safe"
        };
    }
}

/// <summary>
/// Display Topology Information
/// </summary>
public class DisplayTopology
{
    /// <summary>Is NVIDIA GPU available in system?</summary>
    public bool IsNvidiaGPUAvailable { get; set; }

    /// <summary>Number of displays connected to dGPU</summary>
    public int DGPUDisplayCount { get; set; }

    /// <summary>Total display count (estimated)</summary>
    public int TotalDisplayCount { get; set; } = 1; // At least internal display

    /// <summary>Is there an external display on dGPU?</summary>
    public bool HasExternalDisplayOnDGPU { get; set; }

    /// <summary>Is there any external display?</summary>
    public bool HasExternalDisplay { get; set; }
}

/// <summary>
/// Display Topology Recommendation
/// </summary>
public class DisplayTopologyRecommendation
{
    /// <summary>Is iGPU-only mode safe?</summary>
    public bool AllowIGPUOnly { get; set; }

    /// <summary>Is dGPU required?</summary>
    public bool RequiresDGPU { get; set; }

    /// <summary>Why is iGPU-only blocked?</summary>
    public string? BlockedReason { get; set; }

    /// <summary>Recommendation reason</summary>
    public string Reason { get; set; } = string.Empty;
}
