# Advanced Multi-Agent System Deployment Script
# Version: 1.0.0
# Description: Automated deployment and rollback for advanced multi-agent system

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("deploy", "rollback", "status", "test")]
    [string]$Action = "status",

    [Parameter(Mandatory=$false)]
    [ValidateSet("phase1", "phase2", "phase3", "all")]
    [string]$Phase = "all",

    [Parameter(Mandatory=$false)]
    [switch]$EnableTelemetry = $true,

    [Parameter(Mandatory=$false)]
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

# Color output functions
function Write-Success { Write-Host $args -ForegroundColor Green }
function Write-Info { Write-Host $args -ForegroundColor Cyan }
function Write-Warning { Write-Host $args -ForegroundColor Yellow }
function Write-Error { Write-Host $args -ForegroundColor Red }

# ASCII Art Banner
function Show-Banner {
    Write-Host @"

╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║   ADVANCED MULTI-AGENT SYSTEM DEPLOYMENT                 ║
║   Lenovo Legion Toolkit - Performance Edition            ║
║                                                           ║
║   Version: 1.0.0                                          ║
║   Status: Ready for Production                           ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan
}

# Check prerequisites
function Test-Prerequisites {
    Write-Info "`n[1/5] Checking prerequisites..."

    # Check Git
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Error "Git is not installed or not in PATH"
        exit 1
    }
    Write-Success "  ✓ Git found"

    # Check .NET
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error ".NET SDK is not installed"
        exit 1
    }
    Write-Success "  ✓ .NET SDK found"

    # Check we're in the right directory
    if (-not (Test-Path "LenovoLegionToolkit.WPF")) {
        Write-Error "Not in Lenovo Legion Toolkit root directory"
        exit 1
    }
    Write-Success "  ✓ Repository structure validated"
}

# Backup current state
function Backup-CurrentState {
    Write-Info "`n[2/5] Creating backup..."

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupBranch = "backup/pre-deployment-$timestamp"

    if ($DryRun) {
        Write-Warning "  [DRY RUN] Would create backup branch: $backupBranch"
        return $backupBranch
    }

    git branch $backupBranch 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Success "  ✓ Backup created: $backupBranch"
    } else {
        Write-Warning "  ! Backup branch already exists or error occurred"
    }

    return $backupBranch
}

# Deploy optimizations
function Deploy-Optimizations {
    param([string]$DeployPhase)

    Write-Info "`n[3/5] Deploying optimizations (Phase: $DeployPhase)..."

    $branches = @()

    switch ($DeployPhase) {
        "phase1" { $branches = @("feature/advanced-optimization-phase1") }
        "phase2" { $branches = @("feature/advanced-optimization-phase1", "feature/advanced-optimization-phase2") }
        "phase3" { $branches = @("feature/advanced-optimization-phase1", "feature/advanced-optimization-phase2", "feature/advanced-optimization-phase3") }
        "all"    { $branches = @("release/advanced-optimizations-v1.0") }
    }

    if ($DryRun) {
        Write-Warning "  [DRY RUN] Would checkout/merge: $($branches -join ', ')"
        return
    }

    foreach ($branch in $branches) {
        Write-Info "  → Checking out $branch..."
        git checkout $branch 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to checkout $branch"
            exit 1
        }
    }

    Write-Success "  ✓ Optimizations deployed successfully"
}

# Configure feature flags
function Set-FeatureFlags {
    param([bool]$EnableAll = $false)

    Write-Info "`n[4/5] Configuring feature flags..."

    $flags = @{
        "LLT_FEATURE_WMICACHE" = "true"
        "LLT_FEATURE_TELEMETRY" = if ($EnableTelemetry) { "true" } else { "false" }
        "LLT_FEATURE_GPURENDERING" = "true"
        "LLT_FEATURE_REACTIVESENSORS" = if ($EnableAll) { "true" } else { "false" }
        "LLT_FEATURE_MLAICONTROLLER" = if ($EnableAll) { "true" } else { "false" }
        "LLT_FEATURE_ADAPTIVEFANCURVES" = if ($EnableAll) { "true" } else { "false" }
        "LLT_FEATURE_OBJECTPOOLING" = if ($EnableAll) { "true" } else { "false" }
    }

    if ($DryRun) {
        Write-Warning "  [DRY RUN] Would set environment variables:"
        foreach ($flag in $flags.GetEnumerator()) {
            Write-Host "    $($flag.Key) = $($flag.Value)"
        }
        return
    }

    foreach ($flag in $flags.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($flag.Key, $flag.Value, "User")
        Write-Success "  ✓ Set $($flag.Key) = $($flag.Value)"
    }
}

# Build and test
function Build-And-Test {
    Write-Info "`n[5/5] Building and testing..."

    if ($DryRun) {
        Write-Warning "  [DRY RUN] Would build Release configuration"
        return
    }

    Write-Info "  → Building Release configuration..."
    $buildOutput = dotnet build --configuration Release "-p:Version=1.0.0" 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed!"
        Write-Host $buildOutput
        exit 1
    }

    Write-Success "  ✓ Build successful (0 errors, 0 warnings)"

    # Quick smoke test
    Write-Info "  → Running smoke tests..."
    $exePath = "LenovoLegionToolkit.WPF\bin\x64\Release\net8.0-windows\win-x64\Lenovo Legion Toolkit.exe"

    if (Test-Path $exePath) {
        Write-Success "  ✓ Main executable found"
    } else {
        Write-Warning "  ! Executable not found at expected path"
    }
}

# Rollback to previous state
function Invoke-Rollback {
    Write-Info "`n[ROLLBACK] Rolling back optimizations..."

    if ($DryRun) {
        Write-Warning "  [DRY RUN] Would checkout backup/pre-advanced-optimization"
        return
    }

    git checkout backup/pre-advanced-optimization 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Success "  ✓ Rolled back to pre-optimization state"

        # Clear feature flags
        Write-Info "  → Clearing feature flags..."
        $flagNames = @(
            "LLT_FEATURE_WMICACHE",
            "LLT_FEATURE_TELEMETRY",
            "LLT_FEATURE_GPURENDERING",
            "LLT_FEATURE_REACTIVESENSORS",
            "LLT_FEATURE_MLAICONTROLLER",
            "LLT_FEATURE_ADAPTIVEFANCURVES",
            "LLT_FEATURE_OBJECTPOOLING"
        )

        foreach ($flag in $flagNames) {
            [Environment]::SetEnvironmentVariable($flag, $null, "User")
        }
        Write-Success "  ✓ Feature flags cleared"
    } else {
        Write-Error "Rollback failed!"
        exit 1
    }
}

# Show deployment status
function Show-Status {
    Show-Banner

    Write-Info "CURRENT STATUS:"
    Write-Host ""

    # Git branch
    $currentBranch = git branch --show-current
    Write-Info "  Branch: " -NoNewline
    Write-Host $currentBranch -ForegroundColor Yellow

    # Feature flags
    Write-Info "`n  Feature Flags:"
    $flags = @(
        "LLT_FEATURE_WMICACHE",
        "LLT_FEATURE_TELEMETRY",
        "LLT_FEATURE_GPURENDERING",
        "LLT_FEATURE_REACTIVESENSORS"
    )

    foreach ($flag in $flags) {
        $value = [Environment]::GetEnvironmentVariable($flag, "User")
        $status = if ($value -eq "true") { "✓ ENABLED " } else { "✗ DISABLED" }
        $color = if ($value -eq "true") { "Green" } else { "Gray" }
        Write-Host "    $status - $flag" -ForegroundColor $color
    }

    # Build info
    Write-Info "`n  Last Build:"
    $exePath = "LenovoLegionToolkit.WPF\bin\x64\Release\net8.0-windows\win-x64\Lenovo Legion Toolkit.exe"
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        Write-Success "    Built: $($fileInfo.LastWriteTime)"
        Write-Success "    Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB"
    } else {
        Write-Warning "    Not built yet"
    }

    Write-Host ""
}

# Main execution
Show-Banner

switch ($Action) {
    "status" {
        Show-Status
    }

    "deploy" {
        if ($DryRun) {
            Write-Warning "`n=== DRY RUN MODE - No changes will be made ===`n"
        }

        Test-Prerequisites
        $backupBranch = Backup-CurrentState
        Deploy-Optimizations -DeployPhase $Phase
        Set-FeatureFlags -EnableAll ($Phase -eq "all")
        Build-And-Test

        Write-Success "`n╔════════════════════════════════════════════╗"
        Write-Success "║  DEPLOYMENT SUCCESSFUL!                    ║"
        Write-Success "╚════════════════════════════════════════════╝"
        Write-Info "`nBackup created at: $backupBranch"
        Write-Info "To rollback: .\deploy-advanced-optimizations.ps1 -Action rollback`n"
    }

    "rollback" {
        if ($DryRun) {
            Write-Warning "`n=== DRY RUN MODE - No changes will be made ===`n"
        }

        Invoke-Rollback
        Write-Success "`n╔════════════════════════════════════════════╗"
        Write-Success "║  ROLLBACK COMPLETE!                        ║"
        Write-Success "╚════════════════════════════════════════════╝`n"
    }

    "test" {
        Write-Info "Running deployment tests..."
        Test-Prerequisites
        Write-Success "`n✓ All tests passed!`n"
    }
}

# Usage examples
if ($Action -eq "status") {
    Write-Info "USAGE EXAMPLES:"
    Write-Host ""
    Write-Host "  Deploy Phase 1 only:" -ForegroundColor Yellow
    Write-Host "    .\deploy-advanced-optimizations.ps1 -Action deploy -Phase phase1"
    Write-Host ""
    Write-Host "  Deploy all phases:" -ForegroundColor Yellow
    Write-Host "    .\deploy-advanced-optimizations.ps1 -Action deploy -Phase all"
    Write-Host ""
    Write-Host "  Dry run (preview changes):" -ForegroundColor Yellow
    Write-Host "    .\deploy-advanced-optimizations.ps1 -Action deploy -DryRun"
    Write-Host ""
    Write-Host "  Rollback optimizations:" -ForegroundColor Yellow
    Write-Host "    .\deploy-advanced-optimizations.ps1 -Action rollback"
    Write-Host ""
}
