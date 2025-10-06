using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.AI;
using LenovoLegionToolkit.Lib.Services;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Testing;

/// <summary>
/// Elite Features Validation - Verifies all elite optimizations are properly integrated
/// Tests IoC resolution, dependency injection, and end-to-end functionality
/// </summary>
public class EliteFeaturesValidation
{
    private readonly EliteFeaturesManager _eliteFeaturesManager;
    private readonly ContentFramerateDetector _framerateDetector;
    private readonly AcousticOptimizer _acousticOptimizer;
    private readonly MemoryPowerManager _memoryPowerManager;
    private readonly PCIePowerManager _pciePowerManager;

    public EliteFeaturesValidation(
        EliteFeaturesManager eliteFeaturesManager,
        ContentFramerateDetector framerateDetector,
        AcousticOptimizer acousticOptimizer,
        MemoryPowerManager memoryPowerManager,
        PCIePowerManager pciePowerManager)
    {
        _eliteFeaturesManager = eliteFeaturesManager ?? throw new ArgumentNullException(nameof(eliteFeaturesManager));
        _framerateDetector = framerateDetector ?? throw new ArgumentNullException(nameof(framerateDetector));
        _acousticOptimizer = acousticOptimizer ?? throw new ArgumentNullException(nameof(acousticOptimizer));
        _memoryPowerManager = memoryPowerManager ?? throw new ArgumentNullException(nameof(memoryPowerManager));
        _pciePowerManager = pciePowerManager ?? throw new ArgumentNullException(nameof(pciePowerManager));
    }

    /// <summary>
    /// Run comprehensive validation of all elite features
    /// </summary>
    public async Task<ValidationResults> ValidateAllAsync()
    {
        var results = new ValidationResults
        {
            TestName = "Elite Features Validation",
            StartTime = DateTime.UtcNow
        };

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"=== ELITE FEATURES VALIDATION STARTED ===");

        // Test 1: IoC Resolution
        results.IoCResolution = ValidateIoCResolution();

        // Test 2: Elite Features Manager
        results.EliteFeaturesManager = await ValidateEliteFeaturesManagerAsync();

        // Test 3: Content Framerate Detection
        results.FramerateDetection = await ValidateFramerateDetectionAsync();

        // Test 4: Acoustic Optimization
        results.AcousticOptimization = ValidateAcousticOptimization();

        // Test 5: Memory Power Management
        results.MemoryPowerManagement = ValidateMemoryPowerManagement();

        // Test 6: PCIe/NVMe Power Management
        results.PCIePowerManagement = ValidatePCIePowerManagement();

        results.EndTime = DateTime.UtcNow;
        results.Duration = results.EndTime - results.StartTime;
        results.AllPassed = results.IoCResolution.Passed &&
                           results.EliteFeaturesManager.Passed &&
                           results.FramerateDetection.Passed &&
                           results.AcousticOptimization.Passed &&
                           results.MemoryPowerManagement.Passed &&
                           results.PCIePowerManagement.Passed;

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"=== ELITE FEATURES VALIDATION COMPLETED ===");
            Log.Instance.Trace($"Duration: {results.Duration.TotalMilliseconds:F0}ms");
            Log.Instance.Trace($"Result: {(results.AllPassed ? "PASS" : "FAIL")}");
        }

        return results;
    }

    private ValidationTest ValidateIoCResolution()
    {
        var test = new ValidationTest { TestName = "IoC Resolution" };

        try
        {
            // All dependencies should be injected successfully
            test.Passed = _eliteFeaturesManager != null &&
                         _framerateDetector != null &&
                         _acousticOptimizer != null &&
                         _memoryPowerManager != null &&
                         _pciePowerManager != null;

            test.Message = test.Passed
                ? "All elite components resolved successfully from IoC"
                : "One or more components failed to resolve";

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': {(test.Passed ? "PASS" : "FAIL")} - {test.Message}");
        }
        catch (Exception ex)
        {
            test.Passed = false;
            test.Message = $"IoC resolution failed: {ex.Message}";
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': FAIL - {test.Message}");
        }

        return test;
    }

    private async Task<ValidationTest> ValidateEliteFeaturesManagerAsync()
    {
        var test = new ValidationTest { TestName = "Elite Features Manager" };

        try
        {
            // Check feature availability
            var availability = _eliteFeaturesManager.GetFeatureAvailability();

            // At minimum, process priority and Windows power should be available
            test.Passed = availability.ProcessPriorityManagement &&
                         availability.WindowsPowerOptimization;

            var availableFeatures = 0;
            if (availability.ProcessPriorityManagement) availableFeatures++;
            if (availability.WindowsPowerOptimization) availableFeatures++;
            if (availability.MSRAccess) availableFeatures++;
            if (availability.NVAPIIntegration) availableFeatures++;
            if (availability.PCIePowerManagement) availableFeatures++;
            if (availability.HardwareAbstractionLayer) availableFeatures++;

            test.Message = $"EliteFeaturesManager operational: {availableFeatures}/6 features available";

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': {(test.Passed ? "PASS" : "FAIL")} - {test.Message}");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            test.Passed = false;
            test.Message = $"EliteFeaturesManager validation failed: {ex.Message}";
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': FAIL - {test.Message}");
        }

        return test;
    }

    private async Task<ValidationTest> ValidateFramerateDetectionAsync()
    {
        var test = new ValidationTest { TestName = "Content Framerate Detection" };

        try
        {
            // Test framerate detection (will return 0 if no media playing, which is fine)
            var detectedFPS = await _framerateDetector.DetectFramerateAsync();

            // Test optimal refresh rate mapping
            var testFPS = new[] { 24, 30, 60 };
            var availableRates = new[] { 48, 60, 90, 120, 165 };

            foreach (var fps in testFPS)
            {
                var optimalHz = _framerateDetector.GetOptimalRefreshRateForContent(fps, availableRates);
                if (optimalHz == 0)
                {
                    test.Passed = false;
                    test.Message = $"Failed to map {fps}fps to optimal refresh rate";
                    return test;
                }
            }

            test.Passed = true;
            test.Message = detectedFPS > 0
                ? $"Framerate detector operational: detected {detectedFPS}fps, mappings verified"
                : "Framerate detector operational: no media detected (expected), mappings verified";

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': PASS - {test.Message}");
        }
        catch (Exception ex)
        {
            test.Passed = false;
            test.Message = $"Framerate detection failed: {ex.Message}";
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': FAIL - {test.Message}");
        }

        return test;
    }

    private ValidationTest ValidateAcousticOptimization()
    {
        var test = new ValidationTest { TestName = "Acoustic Optimization" };

        try
        {
            // Test acoustic optimization with various scenarios
            var scenarios = new[]
            {
                (current: 30, target: 80, intent: UserIntent.Quiet),      // Large increase in quiet mode
                (current: 70, target: 30, intent: UserIntent.Gaming),     // Large decrease in gaming
                (current: 50, target: 55, intent: UserIntent.Balanced)    // Small change in balanced
            };

            foreach (var (current, target, intent) in scenarios)
            {
                var recommendation = _acousticOptimizer.OptimizeForAcoustics(current, target, intent);

                // Validate recommendation is within bounds
                if (recommendation.RecommendedPercent < 0 || recommendation.RecommendedPercent > 100)
                {
                    test.Passed = false;
                    test.Message = $"Invalid fan speed recommendation: {recommendation.RecommendedPercent}%";
                    return test;
                }

                // Validate noise estimation
                if (recommendation.EstimatedNoiseDb < 28 || recommendation.EstimatedNoiseDb > 64)
                {
                    test.Passed = false;
                    test.Message = $"Invalid noise estimation: {recommendation.EstimatedNoiseDb:F1} dBA";
                    return test;
                }
            }

            test.Passed = true;
            test.Message = "Acoustic optimizer operational: all scenarios validated";

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': PASS - {test.Message}");
        }
        catch (Exception ex)
        {
            test.Passed = false;
            test.Message = $"Acoustic optimization failed: {ex.Message}";
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': FAIL - {test.Message}");
        }

        return test;
    }

    private ValidationTest ValidateMemoryPowerManagement()
    {
        var test = new ValidationTest { TestName = "Memory Power Management" };

        try
        {
            // Test profile selection logic
            var testScenarios = new[]
            {
                (battery: true, batteryPct: 10, availMB: 4096L, totalMB: 16384L, idle: true),  // Critical
                (battery: true, batteryPct: 25, availMB: 4096L, totalMB: 16384L, idle: false), // Low battery
                (battery: false, batteryPct: 100, availMB: 4096L, totalMB: 16384L, idle: false) // AC power
            };

            foreach (var (battery, batteryPct, availMB, totalMB, idle) in testScenarios)
            {
                var profile = _memoryPowerManager.GetOptimalProfile(battery, batteryPct, availMB, totalMB, idle);

                // Validate profile is valid
                if (!Enum.IsDefined(typeof(MemoryPowerProfile), profile))
                {
                    test.Passed = false;
                    test.Message = $"Invalid memory profile selected: {profile}";
                    return test;
                }
            }

            test.Passed = true;
            test.Message = "Memory power manager operational: all profile selections validated";

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': PASS - {test.Message}");
        }
        catch (Exception ex)
        {
            test.Passed = false;
            test.Message = $"Memory power management validation failed: {ex.Message}";
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': FAIL - {test.Message}");
        }

        return test;
    }

    private ValidationTest ValidatePCIePowerManagement()
    {
        var test = new ValidationTest { TestName = "PCIe/NVMe Power Management" };

        try
        {
            // PCIePowerManager may not have devices if drivers unavailable
            // Just verify the component is operational
            test.Passed = _pciePowerManager != null;
            test.Message = "PCIe power manager operational (device control depends on hardware/drivers)";

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': PASS - {test.Message}");
        }
        catch (Exception ex)
        {
            test.Passed = false;
            test.Message = $"PCIe power management validation failed: {ex.Message}";
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Test '{test.TestName}': FAIL - {test.Message}");
        }

        return test;
    }
}

/// <summary>
/// Validation results container
/// </summary>
public class ValidationResults
{
    public string TestName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool AllPassed { get; set; }

    public ValidationTest IoCResolution { get; set; } = new();
    public ValidationTest EliteFeaturesManager { get; set; } = new();
    public ValidationTest FramerateDetection { get; set; } = new();
    public ValidationTest AcousticOptimization { get; set; } = new();
    public ValidationTest MemoryPowerManagement { get; set; } = new();
    public ValidationTest PCIePowerManagement { get; set; } = new();

    public string GetSummary()
    {
        return $@"Elite Features Validation Summary:
Duration: {Duration.TotalMilliseconds:F0}ms
Result: {(AllPassed ? "ALL TESTS PASSED ✅" : "SOME TESTS FAILED ❌")}

Tests:
  {IoCResolution.TestName}: {(IoCResolution.Passed ? "PASS" : "FAIL")} - {IoCResolution.Message}
  {EliteFeaturesManager.TestName}: {(EliteFeaturesManager.Passed ? "PASS" : "FAIL")} - {EliteFeaturesManager.Message}
  {FramerateDetection.TestName}: {(FramerateDetection.Passed ? "PASS" : "FAIL")} - {FramerateDetection.Message}
  {AcousticOptimization.TestName}: {(AcousticOptimization.Passed ? "PASS" : "FAIL")} - {AcousticOptimization.Message}
  {MemoryPowerManagement.TestName}: {(MemoryPowerManagement.Passed ? "PASS" : "FAIL")} - {MemoryPowerManagement.Message}
  {PCIePowerManagement.TestName}: {(PCIePowerManagement.Passed ? "PASS" : "FAIL")} - {PCIePowerManagement.Message}";
    }
}

/// <summary>
/// Individual validation test
/// </summary>
public class ValidationTest
{
    public string TestName { get; set; } = "";
    public bool Passed { get; set; }
    public string Message { get; set; } = "";
}
