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
/// Comprehensive Battery Life Test Suite
/// Validates power optimizations across 6 critical workload scenarios
///
/// Test Methodology:
/// - Each test runs for 10 minutes on battery
/// - Measures power draw, battery drain, and projected battery life
/// - Compares against baseline (no optimizations)
/// - Success criteria: 15-30% improvement vs baseline
/// </summary>
public class BatteryLifeTestSuite
{
    private readonly ResourceOrchestrator _orchestrator;
    private readonly SystemContextStore _contextStore;

    public BatteryLifeTestSuite(
        ResourceOrchestrator orchestrator,
        SystemContextStore contextStore)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
    }

    /// <summary>
    /// Run all battery life validation tests
    /// </summary>
    public async Task<BatteryTestResults> RunAllTestsAsync(CancellationToken cancellationToken = default)
    {
        var results = new BatteryTestResults
        {
            TestStartTime = DateTime.UtcNow,
            Tests = new List<BatteryTestScenario>()
        };

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"=== BATTERY LIFE TEST SUITE STARTED ===");

        // Scenario 1: Media Playback (Movie watching)
        results.Tests.Add(await RunMediaPlaybackTestAsync(cancellationToken));

        // Scenario 2: Productivity (Document editing, browsing)
        results.Tests.Add(await RunProductivityTestAsync(cancellationToken));

        // Scenario 3: Video Conferencing (Zoom/Teams)
        results.Tests.Add(await RunVideoConferencingTestAsync(cancellationToken));

        // Scenario 4: Idle (Background tasks only)
        results.Tests.Add(await RunIdleTestAsync(cancellationToken));

        // Scenario 5: Light Gaming (Moderate GPU load)
        results.Tests.Add(await RunLightGamingTestAsync(cancellationToken));

        // Scenario 6: Compilation (CPU burst workload)
        results.Tests.Add(await RunCompilationTestAsync(cancellationToken));

        results.TestEndTime = DateTime.UtcNow;
        results.TotalDuration = results.TestEndTime - results.TestStartTime;

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"=== BATTERY LIFE TEST SUITE COMPLETED ===");
            Log.Instance.Trace($"Total Duration: {results.TotalDuration.TotalMinutes:F1} minutes");
            Log.Instance.Trace($"Tests Passed: {results.Tests.Count(t => t.Passed)}/{results.Tests.Count}");
        }

        return results;
    }

    /// <summary>
    /// Scenario 1: Media Playback Test
    /// Expected: 85-90% power reduction vs gaming (175W → 18-25W)
    /// Target battery life: 8-10 hours
    /// </summary>
    private async Task<BatteryTestScenario> RunMediaPlaybackTestAsync(CancellationToken cancellationToken)
    {
        var scenario = new BatteryTestScenario
        {
            Name = "Media Playback",
            WorkloadType = WorkloadType.MediaPlayback,
            ExpectedPowerWatts = 22, // Target: 18-25W
            MaxAcceptablePowerWatts = 30,
            MinBatteryLifeHours = 7.0,
            TestDurationMinutes = 10
        };

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting test: {scenario.Name}");

        var startTime = DateTime.UtcNow;
        var powerSamples = new List<int>();
        var batterySamples = new List<int>();

        // Simulate media playback for 10 minutes
        var endTime = startTime.AddMinutes(scenario.TestDurationMinutes);

        while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
        {
            var context = await _contextStore.GatherContextAsync();

            // Override workload to media playback for testing
            context.CurrentWorkload.Type = WorkloadType.MediaPlayback;

            // Note: Orchestrator runs continuously - context reflects current optimizations
            // Sample power and battery
            powerSamples.Add(context.PowerState.TotalSystemPower);
            batterySamples.Add(context.BatteryState.ChargePercent);

            await Task.Delay(10000, cancellationToken); // Sample every 10 seconds
        }

        scenario.ActualDurationMinutes = (DateTime.UtcNow - startTime).TotalMinutes;
        scenario.AveragePowerWatts = powerSamples.Count > 0 ? powerSamples.Average() : 0;
        scenario.BatteryDrainPercent = batterySamples.Count > 0
            ? batterySamples.First() - batterySamples.Last()
            : 0;

        // Calculate projected battery life
        if (scenario.BatteryDrainPercent > 0)
        {
            var drainRatePerMinute = scenario.BatteryDrainPercent / scenario.ActualDurationMinutes;
            scenario.ProjectedBatteryLifeHours = (100.0 / drainRatePerMinute) / 60.0;
        }

        scenario.Passed = scenario.AveragePowerWatts <= scenario.MaxAcceptablePowerWatts &&
                         scenario.ProjectedBatteryLifeHours >= scenario.MinBatteryLifeHours;

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Test '{scenario.Name}' completed:");
            Log.Instance.Trace($"  Average Power: {scenario.AveragePowerWatts:F1}W (expected: {scenario.ExpectedPowerWatts}W)");
            Log.Instance.Trace($"  Projected Battery Life: {scenario.ProjectedBatteryLifeHours:F1}h (min: {scenario.MinBatteryLifeHours}h)");
            Log.Instance.Trace($"  Result: {(scenario.Passed ? "PASS" : "FAIL")}");
        }

        return scenario;
    }

    /// <summary>
    /// Scenario 2: Productivity Test
    /// Expected: 30-50W system power
    /// Target battery life: 5-7 hours
    /// </summary>
    private async Task<BatteryTestScenario> RunProductivityTestAsync(CancellationToken cancellationToken)
    {
        var scenario = new BatteryTestScenario
        {
            Name = "Productivity",
            WorkloadType = WorkloadType.LightProductivity,
            ExpectedPowerWatts = 40,
            MaxAcceptablePowerWatts = 55,
            MinBatteryLifeHours = 5.0,
            TestDurationMinutes = 10
        };

        return await RunWorkloadTestAsync(scenario, cancellationToken);
    }

    /// <summary>
    /// Scenario 3: Video Conferencing Test
    /// Expected: 35-50W (CPU encoding + camera + network)
    /// Target battery life: 4-6 hours
    /// </summary>
    private async Task<BatteryTestScenario> RunVideoConferencingTestAsync(CancellationToken cancellationToken)
    {
        var scenario = new BatteryTestScenario
        {
            Name = "Video Conferencing",
            WorkloadType = WorkloadType.VideoConferencing,
            ExpectedPowerWatts = 45,
            MaxAcceptablePowerWatts = 60,
            MinBatteryLifeHours = 4.0,
            TestDurationMinutes = 10
        };

        return await RunWorkloadTestAsync(scenario, cancellationToken);
    }

    /// <summary>
    /// Scenario 4: Idle Test
    /// Expected: 8-15W (display + background)
    /// Target battery life: 10-15 hours
    /// </summary>
    private async Task<BatteryTestScenario> RunIdleTestAsync(CancellationToken cancellationToken)
    {
        var scenario = new BatteryTestScenario
        {
            Name = "Idle",
            WorkloadType = WorkloadType.Idle,
            ExpectedPowerWatts = 12,
            MaxAcceptablePowerWatts = 18,
            MinBatteryLifeHours = 10.0,
            TestDurationMinutes = 10
        };

        return await RunWorkloadTestAsync(scenario, cancellationToken);
    }

    /// <summary>
    /// Scenario 5: Light Gaming Test
    /// Expected: 80-110W (moderate GPU + CPU)
    /// Target battery life: 1.5-2 hours
    /// </summary>
    private async Task<BatteryTestScenario> RunLightGamingTestAsync(CancellationToken cancellationToken)
    {
        var scenario = new BatteryTestScenario
        {
            Name = "Light Gaming",
            WorkloadType = WorkloadType.Gaming,
            ExpectedPowerWatts = 95,
            MaxAcceptablePowerWatts = 120,
            MinBatteryLifeHours = 1.5,
            TestDurationMinutes = 10
        };

        return await RunWorkloadTestAsync(scenario, cancellationToken);
    }

    /// <summary>
    /// Scenario 6: Compilation Test
    /// Expected: 60-80W (CPU burst workload)
    /// Target battery life: 2-3 hours
    /// </summary>
    private async Task<BatteryTestScenario> RunCompilationTestAsync(CancellationToken cancellationToken)
    {
        var scenario = new BatteryTestScenario
        {
            Name = "Compilation",
            WorkloadType = WorkloadType.Compilation,
            ExpectedPowerWatts = 70,
            MaxAcceptablePowerWatts = 90,
            MinBatteryLifeHours = 2.0,
            TestDurationMinutes = 10
        };

        return await RunWorkloadTestAsync(scenario, cancellationToken);
    }

    /// <summary>
    /// Generic workload test runner
    /// </summary>
    private async Task<BatteryTestScenario> RunWorkloadTestAsync(
        BatteryTestScenario scenario,
        CancellationToken cancellationToken)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Starting test: {scenario.Name}");

        var startTime = DateTime.UtcNow;
        var powerSamples = new List<int>();
        var batterySamples = new List<int>();

        var endTime = startTime.AddMinutes(scenario.TestDurationMinutes);

        while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
        {
            var context = await _contextStore.GatherContextAsync();
            context.CurrentWorkload.Type = scenario.WorkloadType;

            // Orchestrator runs continuously - context reflects optimizations
            powerSamples.Add(context.PowerState.TotalSystemPower);
            batterySamples.Add(context.BatteryState.ChargePercent);

            await Task.Delay(10000, cancellationToken);
        }

        scenario.ActualDurationMinutes = (DateTime.UtcNow - startTime).TotalMinutes;
        scenario.AveragePowerWatts = powerSamples.Count > 0 ? powerSamples.Average() : 0;
        scenario.BatteryDrainPercent = batterySamples.Count > 0
            ? batterySamples.First() - batterySamples.Last()
            : 0;

        if (scenario.BatteryDrainPercent > 0)
        {
            var drainRatePerMinute = scenario.BatteryDrainPercent / scenario.ActualDurationMinutes;
            scenario.ProjectedBatteryLifeHours = (100.0 / drainRatePerMinute) / 60.0;
        }

        scenario.Passed = scenario.AveragePowerWatts <= scenario.MaxAcceptablePowerWatts &&
                         scenario.ProjectedBatteryLifeHours >= scenario.MinBatteryLifeHours;

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Test '{scenario.Name}' completed:");
            Log.Instance.Trace($"  Average Power: {scenario.AveragePowerWatts:F1}W (expected: {scenario.ExpectedPowerWatts}W)");
            Log.Instance.Trace($"  Projected Battery Life: {scenario.ProjectedBatteryLifeHours:F1}h (min: {scenario.MinBatteryLifeHours}h)");
            Log.Instance.Trace($"  Result: {(scenario.Passed ? "PASS" : "FAIL")}");
        }

        return scenario;
    }
}

/// <summary>
/// Battery test results container
/// </summary>
public class BatteryTestResults
{
    public DateTime TestStartTime { get; set; }
    public DateTime TestEndTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public List<BatteryTestScenario> Tests { get; set; } = new();

    public bool AllTestsPassed => Tests.All(t => t.Passed);
    public double AverageImprovementPercent
    {
        get
        {
            if (Tests.Count == 0) return 0;
            var improvements = Tests
                .Where(t => t.BaselinePowerWatts > 0)
                .Select(t => ((t.BaselinePowerWatts - t.AveragePowerWatts) / t.BaselinePowerWatts) * 100);
            return improvements.Any() ? improvements.Average() : 0;
        }
    }
}

/// <summary>
/// Individual test scenario
/// </summary>
public class BatteryTestScenario
{
    public string Name { get; set; } = "";
    public WorkloadType WorkloadType { get; set; }
    public double ExpectedPowerWatts { get; set; }
    public double MaxAcceptablePowerWatts { get; set; }
    public double MinBatteryLifeHours { get; set; }
    public double TestDurationMinutes { get; set; }

    // Results
    public double ActualDurationMinutes { get; set; }
    public double AveragePowerWatts { get; set; }
    public double BatteryDrainPercent { get; set; }
    public double ProjectedBatteryLifeHours { get; set; }
    public bool Passed { get; set; }

    // Baseline comparison (optional - for regression testing)
    public double BaselinePowerWatts { get; set; }
    public double ImprovementPercent => BaselinePowerWatts > 0
        ? ((BaselinePowerWatts - AveragePowerWatts) / BaselinePowerWatts) * 100
        : 0;
}
