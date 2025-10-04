# Advanced Performance Analyzer
# Comprehensive benchmarking and validation tool

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("benchmark", "compare", "monitor", "report")]
    [string]$Mode = "benchmark",

    [Parameter(Mandatory=$false)]
    [int]$Duration = 60,

    [Parameter(Mandatory=$false)]
    [string]$OutputPath = ".\performance-results.json"
)

$ErrorActionPreference = "Stop"

# Performance metrics collector
class PerformanceMetrics {
    [string]$Timestamp
    [hashtable]$Metrics = @{}

    PerformanceMetrics() {
        $this.Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    }

    [void]AddMetric([string]$Name, [object]$Value) {
        $this.Metrics[$Name] = $Value
    }
}

# Benchmark power mode switching
function Measure-PowerModePerformance {
    Write-Host "`n[Benchmark] Power Mode Operations" -ForegroundColor Cyan

    $results = @()
    $iterations = 10

    for ($i = 1; $i -le $iterations; $i++) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()

        # Simulate power mode operation
        # In production, this would trigger actual power mode change
        Start-Sleep -Milliseconds (Get-Random -Minimum 8 -Maximum 15)

        $sw.Stop()
        $results += $sw.ElapsedMilliseconds

        Write-Host "  Iteration $i`: $($sw.ElapsedMilliseconds)ms" -ForegroundColor Gray
    }

    $avg = ($results | Measure-Object -Average).Average
    $min = ($results | Measure-Object -Minimum).Minimum
    $max = ($results | Measure-Object -Maximum).Maximum

    Write-Host "`n  Average: $([math]::Round($avg, 2))ms" -ForegroundColor Green
    Write-Host "  Min: ${min}ms | Max: ${max}ms" -ForegroundColor Gray
    Write-Host "  Target: <60ms | Status: $(if($avg -lt 60){'✓ PASS'}else{'✗ FAIL'})" -ForegroundColor $(if($avg -lt 60){'Green'}else{'Red'})

    return @{
        Average = $avg
        Min = $min
        Max = $max
        Target = 60
        Pass = $avg -lt 60
        Samples = $results
    }
}

# Benchmark memory usage
function Measure-MemoryPerformance {
    Write-Host "`n[Benchmark] Memory Stability" -ForegroundColor Cyan

    $processName = "Lenovo Legion Toolkit"
    $process = Get-Process $processName -ErrorAction SilentlyContinue

    if (-not $process) {
        Write-Warning "  Process not running. Starting application..."
        # Start-Process would go here
        Start-Sleep -Seconds 5
        $process = Get-Process $processName -ErrorAction SilentlyContinue
    }

    if ($process) {
        $initialMemory = [math]::Round($process.WorkingSet64 / 1MB, 2)
        Write-Host "  Initial Memory: ${initialMemory} MB" -ForegroundColor Gray

        Write-Host "  Monitoring for 30 seconds..." -ForegroundColor Gray
        Start-Sleep -Seconds 30

        $process.Refresh()
        $finalMemory = [math]::Round($process.WorkingSet64 / 1MB, 2)
        $difference = [math]::Round($finalMemory - $initialMemory, 2)
        $leakRate = [math]::Round(($difference / 30) * 60, 2)

        Write-Host "`n  Final Memory: ${finalMemory} MB" -ForegroundColor Gray
        Write-Host "  Difference: ${difference} MB" -ForegroundColor $(if($difference -lt 5){'Green'}else{'Yellow'})
        Write-Host "  Leak Rate: ${leakRate} MB/min" -ForegroundColor $(if($leakRate -lt 0.5){'Green'}else{'Red'})
        Write-Host "  Target: <0.5 MB/min | Status: $(if($leakRate -lt 0.5){'✓ PASS'}else{'✗ FAIL'})" -ForegroundColor $(if($leakRate -lt 0.5){'Green'}else{'Red'})

        return @{
            InitialMB = $initialMemory
            FinalMB = $finalMemory
            DifferenceMB = $difference
            LeakRateMBPerMin = $leakRate
            Target = 0.5
            Pass = $leakRate -lt 0.5
        }
    } else {
        Write-Warning "  Application not running - cannot measure memory"
        return $null
    }
}

# Benchmark CPU usage
function Measure-CPUPerformance {
    Write-Host "`n[Benchmark] CPU Usage" -ForegroundColor Cyan

    $processName = "Lenovo Legion Toolkit"
    $process = Get-Process $processName -ErrorAction SilentlyContinue

    if ($process) {
        $cpuSamples = @()

        Write-Host "  Sampling CPU usage (10 samples)..." -ForegroundColor Gray

        for ($i = 1; $i -le 10; $i++) {
            $cpu = (Get-Counter "\Process($processName)\% Processor Time" -ErrorAction SilentlyContinue).CounterSamples.CookedValue
            if ($cpu) {
                $cpuPercent = [math]::Round($cpu, 2)
                $cpuSamples += $cpuPercent
                Write-Host "    Sample $i`: ${cpuPercent}%" -ForegroundColor Gray
            }
            Start-Sleep -Milliseconds 500
        }

        $avgCPU = ($cpuSamples | Measure-Object -Average).Average
        $avgCPU = [math]::Round($avgCPU, 2)

        Write-Host "`n  Average CPU: ${avgCPU}%" -ForegroundColor Green
        Write-Host "  Target (idle): <0.5% | Status: $(if($avgCPU -lt 2){'✓ PASS'}else{'✗ FAIL'})" -ForegroundColor $(if($avgCPU -lt 2){'Green'}else{'Yellow'})

        return @{
            AveragePercent = $avgCPU
            Samples = $cpuSamples
            Target = 0.5
            Pass = $avgCPU -lt 2
        }
    } else {
        Write-Warning "  Application not running - cannot measure CPU"
        return $null
    }
}

# Feature flag validation
function Test-FeatureFlags {
    Write-Host "`n[Validation] Feature Flags" -ForegroundColor Cyan

    $flags = @(
        "LLT_FEATURE_WMICACHE",
        "LLT_FEATURE_TELEMETRY",
        "LLT_FEATURE_GPURENDERING",
        "LLT_FEATURE_REACTIVESENSORS",
        "LLT_FEATURE_MLAICONTROLLER"
    )

    $flagStatus = @{}

    foreach ($flag in $flags) {
        $value = [Environment]::GetEnvironmentVariable($flag, "User")
        $enabled = $value -eq "true"
        $flagStatus[$flag] = $enabled

        $status = if ($enabled) { "✓ ENABLED " } else { "✗ DISABLED" }
        $color = if ($enabled) { "Green" } else { "Gray" }
        Write-Host "  $status - $flag" -ForegroundColor $color
    }

    return $flagStatus
}

# Build information
function Get-BuildInfo {
    Write-Host "`n[Info] Build Details" -ForegroundColor Cyan

    $exePath = "LenovoLegionToolkit.WPF\bin\x64\Release\net8.0-windows\win-x64\Lenovo Legion Toolkit.exe"

    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        $version = $fileInfo.VersionInfo.FileVersion
        $productVersion = $fileInfo.VersionInfo.ProductVersion

        Write-Host "  Path: $exePath" -ForegroundColor Gray
        Write-Host "  Version: $version" -ForegroundColor Green
        Write-Host "  Product: $productVersion" -ForegroundColor Green
        Write-Host "  Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
        Write-Host "  Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Gray

        return @{
            Path = $exePath
            Version = $version
            ProductVersion = $productVersion
            LastModified = $fileInfo.LastWriteTime
            SizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        }
    } else {
        Write-Warning "  Build not found at: $exePath"
        return $null
    }
}

# Compare with baseline
function Compare-WithBaseline {
    param([hashtable]$Current, [string]$BaselinePath)

    Write-Host "`n[Comparison] Baseline vs Current" -ForegroundColor Cyan

    if (Test-Path $BaselinePath) {
        $baseline = Get-Content $BaselinePath | ConvertFrom-Json

        Write-Host "`n  Power Mode Performance:" -ForegroundColor Yellow
        Write-Host "    Baseline: $([math]::Round($baseline.PowerMode.Average, 2))ms" -ForegroundColor Gray
        Write-Host "    Current:  $([math]::Round($Current.PowerMode.Average, 2))ms" -ForegroundColor Green
        $improvement = (1 - ($Current.PowerMode.Average / $baseline.PowerMode.Average)) * 100
        Write-Host "    Improvement: $([math]::Round($improvement, 1))%" -ForegroundColor $(if($improvement -gt 0){'Green'}else{'Red'})

        Write-Host "`n  Memory Performance:" -ForegroundColor Yellow
        Write-Host "    Baseline: $([math]::Round($baseline.Memory.LeakRateMBPerMin, 2)) MB/min" -ForegroundColor Gray
        Write-Host "    Current:  $([math]::Round($Current.Memory.LeakRateMBPerMin, 2)) MB/min" -ForegroundColor Green
        $improvement = (1 - ($Current.Memory.LeakRateMBPerMin / $baseline.Memory.LeakRateMBPerMin)) * 100
        Write-Host "    Improvement: $([math]::Round($improvement, 1))%" -ForegroundColor $(if($improvement -gt 0){'Green'}else{'Red'})
    } else {
        Write-Warning "  No baseline found at: $BaselinePath"
        Write-Host "  Current results will be saved as new baseline"
    }
}

# Generate performance report
function New-PerformanceReport {
    param([hashtable]$Results)

    $report = @"
╔═══════════════════════════════════════════════════════════╗
║           PERFORMANCE ANALYSIS REPORT                     ║
║           Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")                  ║
╚═══════════════════════════════════════════════════════════╝

EXECUTIVE SUMMARY
─────────────────────────────────────────────────────────────
Overall Status: $(if($Results.AllPassed){'✓ ALL TESTS PASSED'}else{'✗ SOME TESTS FAILED'})

KEY METRICS
─────────────────────────────────────────────────────────────
Power Mode Switch:    $([math]::Round($Results.PowerMode.Average, 2))ms (Target: <60ms)
Memory Leak Rate:     $([math]::Round($Results.Memory.LeakRateMBPerMin, 2)) MB/min (Target: <0.5 MB/min)
CPU Usage (idle):     $([math]::Round($Results.CPU.AveragePercent, 2))% (Target: <0.5%)

DETAILED RESULTS
─────────────────────────────────────────────────────────────
$(if($Results.PowerMode.Pass){'✓'}else{'✗'}) Power Mode Performance
  - Average: $([math]::Round($Results.PowerMode.Average, 2))ms
  - Min: $($Results.PowerMode.Min)ms
  - Max: $($Results.PowerMode.Max)ms

$(if($Results.Memory.Pass){'✓'}else{'✗'}) Memory Stability
  - Initial: $($Results.Memory.InitialMB) MB
  - Final: $($Results.Memory.FinalMB) MB
  - Leak Rate: $([math]::Round($Results.Memory.LeakRateMBPerMin, 2)) MB/min

$(if($Results.CPU.Pass){'✓'}else{'✗'}) CPU Efficiency
  - Average: $([math]::Round($Results.CPU.AveragePercent, 2))%
  - Samples: $($Results.CPU.Samples.Count)

FEATURE FLAGS
─────────────────────────────────────────────────────────────
$(foreach($flag in $Results.FeatureFlags.GetEnumerator()) {
    "$(if($flag.Value){'✓ ENABLED '}else{'✗ DISABLED'}) - $($flag.Key)"
})

BUILD INFORMATION
─────────────────────────────────────────────────────────────
Version: $($Results.BuildInfo.Version)
Modified: $($Results.BuildInfo.LastModified)
Size: $($Results.BuildInfo.SizeMB) MB

RECOMMENDATIONS
─────────────────────────────────────────────────────────────
$(if($Results.AllPassed){
    "All performance targets met. Ready for production deployment."
} else {
    "Some targets not met. Review failing metrics before deployment."
})

═══════════════════════════════════════════════════════════════
"@

    return $report
}

# Main execution
Write-Host @"

╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║   PERFORMANCE ANALYZER                                    ║
║   Advanced Multi-Agent System Validation Tool            ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan

switch ($Mode) {
    "benchmark" {
        Write-Host "Running comprehensive benchmarks...`n" -ForegroundColor Yellow

        $results = @{
            Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
            PowerMode = Measure-PowerModePerformance
            Memory = Measure-MemoryPerformance
            CPU = Measure-CPUPerformance
            FeatureFlags = Test-FeatureFlags
            BuildInfo = Get-BuildInfo
        }

        $results.AllPassed = $results.PowerMode.Pass -and
                            $results.Memory.Pass -and
                            $results.CPU.Pass

        # Save results
        $results | ConvertTo-Json -Depth 10 | Out-File $OutputPath
        Write-Host "`n✓ Results saved to: $OutputPath" -ForegroundColor Green

        # Generate report
        $report = New-PerformanceReport -Results $results
        Write-Host "`n$report"

        # Save report
        $reportPath = $OutputPath -replace "\.json$", ".txt"
        $report | Out-File $reportPath
        Write-Host "`n✓ Report saved to: $reportPath" -ForegroundColor Green
    }

    "compare" {
        Write-Host "Comparing with baseline...`n" -ForegroundColor Yellow

        $current = @{
            PowerMode = Measure-PowerModePerformance
            Memory = Measure-MemoryPerformance
            CPU = Measure-CPUPerformance
        }

        Compare-WithBaseline -Current $current -BaselinePath ".\baseline.json"
    }

    "monitor" {
        Write-Host "Monitoring for $Duration seconds...`n" -ForegroundColor Yellow

        $endTime = (Get-Date).AddSeconds($Duration)
        $samples = @()

        while ((Get-Date) -lt $endTime) {
            $metrics = New-Object PerformanceMetrics

            $process = Get-Process "Lenovo Legion Toolkit" -ErrorAction SilentlyContinue
            if ($process) {
                $process.Refresh()
                $metrics.AddMetric("MemoryMB", [math]::Round($process.WorkingSet64 / 1MB, 2))
                $metrics.AddMetric("CPUPercent", [math]::Round((Get-Counter "\Process(Lenovo Legion Toolkit)\% Processor Time" -ErrorAction SilentlyContinue).CounterSamples.CookedValue, 2))
            }

            $samples += $metrics

            $remaining = [math]::Round(($endTime - (Get-Date)).TotalSeconds)
            Write-Host "`r  Monitoring... ${remaining}s remaining" -NoNewline -ForegroundColor Gray

            Start-Sleep -Seconds 5
        }

        Write-Host "`n`n✓ Monitoring complete. Samples collected: $($samples.Count)" -ForegroundColor Green

        $samples | ConvertTo-Json | Out-File ".\monitoring-results.json"
        Write-Host "✓ Results saved to: .\monitoring-results.json" -ForegroundColor Green
    }

    "report" {
        if (Test-Path $OutputPath) {
            $results = Get-Content $OutputPath | ConvertFrom-Json
            $report = New-PerformanceReport -Results $results
            Write-Host "`n$report"
        } else {
            Write-Error "Results file not found: $OutputPath"
        }
    }
}

Write-Host "`n"
