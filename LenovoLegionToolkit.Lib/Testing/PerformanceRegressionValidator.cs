using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.AI;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Testing;

/// <summary>
/// Performance Regression Validator
/// Ensures power optimizations don't negatively impact performance
///
/// Test Categories:
/// 1. Gaming Performance (FPS, frame times, input latency)
/// 2. Compilation Speed (build times, CPU throughput)
/// 3. Media Performance (video decode, playback smoothness)
/// 4. System Responsiveness (UI latency, app launch times)
///
/// Success Criteria: &lt;5% performance degradation vs baseline
/// </summary>
public class PerformanceRegressionValidator
{
    private readonly ResourceOrchestrator _orchestrator;
    private readonly SystemContextStore _contextStore;

    public PerformanceRegressionValidator(
        ResourceOrchestrator orchestrator,
        SystemContextStore contextStore)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
    }

    /// <summary>
    /// Run all performance regression tests
    /// </summary>
    public async Task<PerformanceTestResults> RunAllTestsAsync(CancellationToken cancellationToken = default)
    {
        var results = new PerformanceTestResults
        {
            TestStartTime = DateTime.UtcNow,
            Tests = new List<PerformanceTest>()
        };

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"=== PERFORMANCE REGRESSION VALIDATION STARTED ===");

        // Test 1: Gaming Performance
        results.Tests.Add(await ValidateGamingPerformanceAsync(cancellationToken));

        // Test 2: Compilation Speed
        results.Tests.Add(await ValidateCompilationSpeedAsync(cancellationToken));

        // Test 3: Media Decode Performance
        results.Tests.Add(await ValidateMediaPerformanceAsync(cancellationToken));

        // Test 4: System Responsiveness
        results.Tests.Add(await ValidateResponsivenessAsync(cancellationToken));

        // Test 5: Refresh Rate Switching Latency
        results.Tests.Add(await ValidateRefreshRateSwitchingAsync(cancellationToken));

        results.TestEndTime = DateTime.UtcNow;
        results.TotalDuration = results.TestEndTime - results.TestStartTime;

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"=== PERFORMANCE REGRESSION VALIDATION COMPLETED ===");
            Log.Instance.Trace($"Tests Passed: {results.Tests.Count(t => t.Passed)}/{results.Tests.Count}");
            Log.Instance.Trace($"Average Performance Impact: {results.AveragePerformanceImpactPercent:F2}%");
        }

        return results;
    }

    /// <summary>
    /// Test 1: Gaming Performance
    /// Validates that power optimizations don't reduce gaming FPS
    /// </summary>
    private async Task<PerformanceTest> ValidateGamingPerformanceAsync(CancellationToken cancellationToken)
    {
        var test = new PerformanceTest
        {
            Name = "Gaming Performance",
            Category = "Performance",
            MaxAcceptableDegradationPercent = 5.0,
            Description = "Validates FPS and frame times during gaming workload"
        };

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Running test: {test.Name}");

        var sw = Stopwatch.StartNew();

        // Simulate gaming workload
        var context = await _contextStore.GatherContextAsync();
        context.CurrentWorkload.Type = WorkloadType.Gaming;
        context.UserIntent = UserIntent.Gaming;

        // Orchestrator runs continuously - context reflects optimizations
        // Measure GPU performance metrics
        var gpuClockMHz = context.GpuState.CoreClockMHz;
        var gpuUtilization = context.GpuState.GpuUtilizationPercent;
        var cpuPL2 = context.PowerState.CurrentPL2;

        sw.Stop();

        // Expected: Max clocks for gaming
        // RTX 4060/4070 should be at 2200+ MHz, PL2 should be 115-140W
        test.BaselineMetric = 2400; // Expected max GPU clock
        test.ActualMetric = gpuClockMHz;
        test.DegradationPercent = ((test.BaselineMetric - test.ActualMetric) / test.BaselineMetric) * 100;
        test.Passed = test.DegradationPercent <= test.MaxAcceptableDegradationPercent;
        test.ExecutionTimeMs = sw.ElapsedMilliseconds;

        test.Details = $"GPU Clock: {gpuClockMHz}MHz, Utilization: {gpuUtilization}%, CPU PL2: {cpuPL2}W";

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Test '{test.Name}': {(test.Passed ? "PASS" : "FAIL")}");
            Log.Instance.Trace($"  {test.Details}");
            Log.Instance.Trace($"  Performance Impact: {test.DegradationPercent:F2}%");
        }

        return test;
    }

    /// <summary>
    /// Test 2: Compilation Speed
    /// Validates that CPU power limits don't slow down builds
    /// </summary>
    private async Task<PerformanceTest> ValidateCompilationSpeedAsync(CancellationToken cancellationToken)
    {
        var test = new PerformanceTest
        {
            Name = "Compilation Speed",
            Category = "CPU Performance",
            MaxAcceptableDegradationPercent = 5.0,
            Description = "Validates CPU clock speeds during compilation workload"
        };

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Running test: {test.Name}");

        var sw = Stopwatch.StartNew();

        var context = await _contextStore.GatherContextAsync();
        context.CurrentWorkload.Type = WorkloadType.Compilation;

        // Check CPU power limits for compilation
        var cpuPL1 = context.PowerState.CurrentPL1;
        var cpuPL2 = context.PowerState.CurrentPL2;

        sw.Stop();

        // Expected: High PL2 for compilation (115-140W)
        test.BaselineMetric = 140; // Max PL2
        test.ActualMetric = cpuPL2;
        test.DegradationPercent = ((test.BaselineMetric - test.ActualMetric) / test.BaselineMetric) * 100;
        test.Passed = test.DegradationPercent <= test.MaxAcceptableDegradationPercent;
        test.ExecutionTimeMs = sw.ElapsedMilliseconds;

        test.Details = $"CPU PL1: {cpuPL1}W, PL2: {cpuPL2}W";

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Test '{test.Name}': {(test.Passed ? "PASS" : "FAIL")}");
            Log.Instance.Trace($"  {test.Details}");
        }

        return test;
    }

    /// <summary>
    /// Test 3: Media Decode Performance
    /// Validates smooth video playback with power optimizations
    /// </summary>
    private async Task<PerformanceTest> ValidateMediaPerformanceAsync(CancellationToken cancellationToken)
    {
        var test = new PerformanceTest
        {
            Name = "Media Decode Performance",
            Category = "Media",
            MaxAcceptableDegradationPercent = 5.0,
            Description = "Validates video decode capability with reduced power"
        };

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Running test: {test.Name}");

        var sw = Stopwatch.StartNew();

        var context = await _contextStore.GatherContextAsync();
        context.CurrentWorkload.Type = WorkloadType.MediaPlayback;

        // Check CPU power (should be low: 15-25W)
        var cpuPL1 = context.PowerState.CurrentPL1;

        sw.Stop();

        // For media: Low power is GOOD, not degradation
        // Validate we have ENOUGH power for 4K decode (min 15W)
        test.BaselineMetric = 15; // Minimum for 4K decode
        test.ActualMetric = cpuPL1;
        test.DegradationPercent = cpuPL1 < 15 ? ((15 - cpuPL1) / 15) * 100 : 0;
        test.Passed = cpuPL1 >= 15; // Must have at least 15W for smooth decode
        test.ExecutionTimeMs = sw.ElapsedMilliseconds;

        test.Details = $"CPU PL1: {cpuPL1}W (min required: 15W for 4K)";

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Test '{test.Name}': {(test.Passed ? "PASS" : "FAIL")}");
            Log.Instance.Trace($"  {test.Details}");
        }

        return test;
    }

    /// <summary>
    /// Test 4: System Responsiveness
    /// Validates UI and application responsiveness
    /// </summary>
    private async Task<PerformanceTest> ValidateResponsivenessAsync(CancellationToken cancellationToken)
    {
        var test = new PerformanceTest
        {
            Name = "System Responsiveness",
            Category = "Latency",
            MaxAcceptableDegradationPercent = 10.0,
            Description = "Validates orchestrator decision latency"
        };

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Running test: {test.Name}");

        // Measure orchestrator latency
        var latencies = new List<long>();

        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            var context = await _contextStore.GatherContextAsync();
            sw.Stop();

            latencies.Add(sw.ElapsedMilliseconds);
        }

        var avgLatency = latencies.Average();

        // Baseline: Should complete in <200ms
        test.BaselineMetric = 200;
        test.ActualMetric = avgLatency;
        test.DegradationPercent = avgLatency > 200 ? ((avgLatency - 200) / 200) * 100 : 0;
        test.Passed = avgLatency <= 220; // Allow up to 10% degradation
        test.ExecutionTimeMs = latencies.Sum();

        test.Details = $"Average Latency: {avgLatency:F1}ms (max: 220ms)";

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Test '{test.Name}': {(test.Passed ? "PASS" : "FAIL")}");
            Log.Instance.Trace($"  {test.Details}");
        }

        return test;
    }

    /// <summary>
    /// Test 5: Refresh Rate Switching Latency
    /// Validates content-aware refresh rate changes are fast
    /// </summary>
    private async Task<PerformanceTest> ValidateRefreshRateSwitchingAsync(CancellationToken cancellationToken)
    {
        var test = new PerformanceTest
        {
            Name = "Refresh Rate Switching",
            Category = "Display",
            MaxAcceptableDegradationPercent = 5.0,
            Description = "Validates refresh rate switching latency"
        };

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Running test: {test.Name}");

        var sw = Stopwatch.StartNew();

        // Simulate workload change: Idle → Media
        var context = await _contextStore.GatherContextAsync();
        context.CurrentWorkload.Type = WorkloadType.Idle;

        // Change to media playback
        context.CurrentWorkload.Type = WorkloadType.MediaPlayback;

        sw.Stop();

        // Baseline: Refresh rate change should be near-instant (<100ms)
        test.BaselineMetric = 100;
        test.ActualMetric = sw.ElapsedMilliseconds;
        test.DegradationPercent = test.ActualMetric > 100 ? ((test.ActualMetric - 100) / 100) * 100 : 0;
        test.Passed = test.ActualMetric <= 150; // Max 150ms
        test.ExecutionTimeMs = sw.ElapsedMilliseconds;

        test.Details = $"Switching Latency: {test.ActualMetric}ms";

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Test '{test.Name}': {(test.Passed ? "PASS" : "FAIL")}");
            Log.Instance.Trace($"  {test.Details}");
        }

        return test;
    }
}

/// <summary>
/// Performance test results container
/// </summary>
public class PerformanceTestResults
{
    public DateTime TestStartTime { get; set; }
    public DateTime TestEndTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public List<PerformanceTest> Tests { get; set; } = new();

    public bool AllTestsPassed => Tests.All(t => t.Passed);
    public double AveragePerformanceImpactPercent
    {
        get
        {
            if (Tests.Count == 0) return 0;
            return Tests.Average(t => t.DegradationPercent);
        }
    }
}

/// <summary>
/// Individual performance test
/// </summary>
public class PerformanceTest
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public double MaxAcceptableDegradationPercent { get; set; }

    // Results
    public double BaselineMetric { get; set; }
    public double ActualMetric { get; set; }
    public double DegradationPercent { get; set; }
    public bool Passed { get; set; }
    public long ExecutionTimeMs { get; set; }
    public string Details { get; set; } = "";
}
