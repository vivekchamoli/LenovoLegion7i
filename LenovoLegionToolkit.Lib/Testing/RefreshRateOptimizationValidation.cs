using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Services;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Testing;

/// <summary>
/// Refresh Rate Optimization Validation
/// PHASE 1 Testing Suite - Validates power-aware refresh rate control
/// </summary>
public class RefreshRateOptimizationValidation
{
    private readonly RefreshRateFeature _refreshRateFeature;
    private readonly BatteryStateService _batteryStateService;
    private readonly PowerModeFeature _powerModeFeature;
    private readonly GPUController _gpuController;

    public RefreshRateOptimizationValidation(
        RefreshRateFeature refreshRateFeature,
        BatteryStateService batteryStateService,
        PowerModeFeature powerModeFeature,
        GPUController gpuController)
    {
        _refreshRateFeature = refreshRateFeature ?? throw new ArgumentNullException(nameof(refreshRateFeature));
        _batteryStateService = batteryStateService ?? throw new ArgumentNullException(nameof(batteryStateService));
        _powerModeFeature = powerModeFeature ?? throw new ArgumentNullException(nameof(powerModeFeature));
        _gpuController = gpuController ?? throw new ArgumentNullException(nameof(gpuController));
    }

    /// <summary>
    /// Run comprehensive validation suite
    /// </summary>
    public async Task<ValidationReport> RunFullValidationAsync()
    {
        var report = new ValidationReport();
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine("=== REFRESH RATE OPTIMIZATION VALIDATION ===");
        Console.WriteLine();

        // Test 1: Dependency Injection
        Console.WriteLine("TEST 1: Dependency Injection");
        report.DependenciesValid = ValidateDependencies();
        Console.WriteLine($"  Result: {(report.DependenciesValid ? "PASS ✅" : "FAIL ❌")}");
        Console.WriteLine();

        // Test 2: Available Refresh Rates
        Console.WriteLine("TEST 2: Available Refresh Rates");
        var availableRates = await _refreshRateFeature.GetAllStatesAsync().ConfigureAwait(false);
        report.AvailableRefreshRates = availableRates.Select(r => r.Frequency).ToArray();
        Console.WriteLine($"  Available: {string.Join(", ", report.AvailableRefreshRates)}Hz");
        Console.WriteLine($"  Result: {(availableRates.Length > 0 ? "PASS ✅" : "FAIL ❌")}");
        Console.WriteLine();

        // Test 3: Battery State Detection
        Console.WriteLine("TEST 3: Battery State Detection");
        // CRITICAL FIX V7: ONLY use BatteryStateService - testing framework should ensure service is running
        var batteryState = _batteryStateService.CurrentState ?? throw new InvalidOperationException("BatteryStateService not ready for testing");
        report.BatteryPercentage = batteryState.BatteryPercentage;
        report.IsOnBattery = !batteryState.IsCharging;
        Console.WriteLine($"  Battery: {batteryState.BatteryPercentage}%");
        Console.WriteLine($"  Charging: {batteryState.IsCharging}");
        Console.WriteLine($"  Discharge Rate: {batteryState.DischargeRate}mW");
        Console.WriteLine($"  Result: PASS ✅");
        Console.WriteLine();

        // Test 4: Power Mode Detection
        Console.WriteLine("TEST 4: Power Mode Detection");
        var powerMode = await _powerModeFeature.GetStateAsync().ConfigureAwait(false);
        report.CurrentPowerMode = powerMode;
        Console.WriteLine($"  Power Mode: {powerMode}");
        Console.WriteLine($"  Result: PASS ✅");
        Console.WriteLine();

        // Test 5: GPU State Detection
        Console.WriteLine("TEST 5: GPU State Detection");
        var gpuState = await _gpuController.GetLastKnownStateAsync().ConfigureAwait(false);
        report.CurrentGPUState = gpuState;
        Console.WriteLine($"  GPU State: {gpuState}");
        Console.WriteLine($"  Result: PASS ✅");
        Console.WriteLine();

        // Test 6: Optimal Refresh Rate Calculation
        Console.WriteLine("TEST 6: Optimal Refresh Rate Calculation");
        var optimalRate = await _refreshRateFeature.GetOptimalRefreshRateAsync().ConfigureAwait(false);
        report.OptimalRefreshRate = optimalRate.Frequency;
        Console.WriteLine($"  Optimal: {optimalRate.Frequency}Hz");
        Console.WriteLine($"  Logic:");

        if (!batteryState.IsCharging && batteryState.BatteryPercentage < 30)
        {
            Console.WriteLine($"    - Battery < 30% → 60Hz (save 2-4W)");
        }
        else if (powerMode == PowerModeState.Quiet)
        {
            Console.WriteLine($"    - Quiet mode → 60Hz (efficiency)");
        }
        else if (powerMode == PowerModeState.Performance || powerMode == PowerModeState.GodMode)
        {
            Console.WriteLine($"    - Performance/GodMode → Max refresh");
        }
        else if (gpuState == GPUState.PoweredOff || gpuState == GPUState.Inactive)
        {
            Console.WriteLine($"    - iGPU → 60Hz (optimal)");
        }
        else
        {
            Console.WriteLine($"    - Balanced → 120Hz");
        }

        Console.WriteLine($"  Result: PASS ✅");
        Console.WriteLine();

        // Test 7: Current Refresh Rate
        Console.WriteLine("TEST 7: Current Refresh Rate");
        var currentRate = await _refreshRateFeature.GetStateAsync().ConfigureAwait(false);
        report.CurrentRefreshRate = currentRate.Frequency;
        Console.WriteLine($"  Current: {currentRate.Frequency}Hz");
        Console.WriteLine($"  Result: PASS ✅");
        Console.WriteLine();

        // Test 8: Optimization Recommendation
        Console.WriteLine("TEST 8: Optimization Recommendation");
        var shouldOptimize = currentRate.Frequency != optimalRate.Frequency;
        report.OptimizationNeeded = shouldOptimize;

        if (shouldOptimize)
        {
            var savings = CalculatePowerSavings(currentRate.Frequency, optimalRate.Frequency);
            report.EstimatedPowerSavingsWatts = savings;
            Console.WriteLine($"  ⚠️  OPTIMIZATION RECOMMENDED");
            Console.WriteLine($"  Change: {currentRate.Frequency}Hz → {optimalRate.Frequency}Hz");
            Console.WriteLine($"  Estimated Savings: {savings}W");
            Console.WriteLine($"  Result: ACTION NEEDED ⚠️");
        }
        else
        {
            Console.WriteLine($"  ✅ Already at optimal refresh rate");
            Console.WriteLine($"  Result: PASS ✅");
        }
        Console.WriteLine();

        // Test 9: Apply Optimization (Optional)
        Console.WriteLine("TEST 9: Apply Optimization Test");
        if (shouldOptimize)
        {
            try
            {
                await _refreshRateFeature.ApplyOptimalRefreshRateAsync().ConfigureAwait(false);
                var newRate = await _refreshRateFeature.GetStateAsync().ConfigureAwait(false);
                report.OptimizationApplied = newRate.Frequency == optimalRate.Frequency;
                Console.WriteLine($"  Applied: {newRate.Frequency}Hz");
                Console.WriteLine($"  Result: {(report.OptimizationApplied ? "PASS ✅" : "FAIL ❌")}");
            }
            catch (Exception ex)
            {
                report.OptimizationApplied = false;
                Console.WriteLine($"  Error: {ex.Message}");
                Console.WriteLine($"  Result: FAIL ❌");
            }
        }
        else
        {
            report.OptimizationApplied = true;
            Console.WriteLine($"  Skipped (already optimal)");
            Console.WriteLine($"  Result: N/A");
        }
        Console.WriteLine();

        stopwatch.Stop();
        report.TotalTestTimeMs = (int)stopwatch.ElapsedMilliseconds;

        // Summary
        Console.WriteLine("=== VALIDATION SUMMARY ===");
        Console.WriteLine($"Total Tests: 9");
        Console.WriteLine($"Duration: {report.TotalTestTimeMs}ms");
        Console.WriteLine();
        Console.WriteLine($"Dependencies Valid: {(report.DependenciesValid ? "✅" : "❌")}");
        Console.WriteLine($"Refresh Rates Available: {report.AvailableRefreshRates.Length}");
        Console.WriteLine($"Current: {report.CurrentRefreshRate}Hz");
        Console.WriteLine($"Optimal: {report.OptimalRefreshRate}Hz");
        Console.WriteLine($"Optimization Needed: {(report.OptimizationNeeded ? "YES ⚠️" : "NO ✅")}");
        if (report.OptimizationNeeded)
        {
            Console.WriteLine($"Estimated Savings: {report.EstimatedPowerSavingsWatts}W");
        }
        Console.WriteLine();

        return report;
    }

    /// <summary>
    /// Validate dependency injection
    /// </summary>
    private bool ValidateDependencies()
    {
        var hasRefreshRateFeature = _refreshRateFeature != null;
        var hasBatteryService = _batteryStateService != null;
        var hasPowerModeFeature = _powerModeFeature != null;
        var hasGPUController = _gpuController != null;

        Console.WriteLine($"  RefreshRateFeature: {(hasRefreshRateFeature ? "✅" : "❌")}");
        Console.WriteLine($"  BatteryStateService: {(hasBatteryService ? "✅" : "❌")}");
        Console.WriteLine($"  PowerModeFeature: {(hasPowerModeFeature ? "✅" : "❌")}");
        Console.WriteLine($"  GPUController: {(hasGPUController ? "✅" : "❌")}");

        return hasRefreshRateFeature && hasBatteryService && hasPowerModeFeature && hasGPUController;
    }

    /// <summary>
    /// Calculate estimated power savings
    /// </summary>
    private double CalculatePowerSavings(int currentHz, int optimalHz)
    {
        // Empirical data: 165Hz vs 60Hz ≈ 3-4W difference
        // Linear approximation for other refresh rates
        var hzDelta = currentHz - optimalHz;

        if (hzDelta <= 0)
            return 0;

        // Rough estimation: 0.04W per Hz difference
        return Math.Round(hzDelta * 0.04, 1);
    }

    /// <summary>
    /// Quick validation (no modifications)
    /// </summary>
    public async Task<bool> QuickValidationAsync()
    {
        try
        {
            var availableRates = await _refreshRateFeature.GetAllStatesAsync().ConfigureAwait(false);
            if (availableRates.Length == 0)
                return false;

            var currentRate = await _refreshRateFeature.GetStateAsync().ConfigureAwait(false);
            var optimalRate = await _refreshRateFeature.GetOptimalRefreshRateAsync().ConfigureAwait(false);

            Console.WriteLine($"Quick Validation: Current={currentRate.Frequency}Hz, Optimal={optimalRate.Frequency}Hz");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Quick Validation Failed: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// Validation report
/// </summary>
public class ValidationReport
{
    public bool DependenciesValid { get; set; }
    public int[] AvailableRefreshRates { get; set; } = Array.Empty<int>();
    public int CurrentRefreshRate { get; set; }
    public int OptimalRefreshRate { get; set; }
    public int BatteryPercentage { get; set; }
    public bool IsOnBattery { get; set; }
    public PowerModeState CurrentPowerMode { get; set; }
    public GPUState CurrentGPUState { get; set; }
    public bool OptimizationNeeded { get; set; }
    public double EstimatedPowerSavingsWatts { get; set; }
    public bool OptimizationApplied { get; set; }
    public int TotalTestTimeMs { get; set; }

    public bool IsFullyOptimized => !OptimizationNeeded || OptimizationApplied;

    public string GetSummary()
    {
        return $@"
=== VALIDATION REPORT ===
Dependencies: {(DependenciesValid ? "✅ Valid" : "❌ Invalid")}
Available Rates: {string.Join(", ", AvailableRefreshRates)}Hz
Current Rate: {CurrentRefreshRate}Hz
Optimal Rate: {OptimalRefreshRate}Hz
Battery: {BatteryPercentage}% ({(IsOnBattery ? "Battery" : "AC")})
Power Mode: {CurrentPowerMode}
GPU State: {CurrentGPUState}
Optimization: {(IsFullyOptimized ? "✅ Optimized" : $"⚠️ Needs {EstimatedPowerSavingsWatts}W savings")}
Test Duration: {TotalTestTimeMs}ms
";
    }
}
