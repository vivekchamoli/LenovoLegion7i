# Load the DLL
$dll = [System.Reflection.Assembly]::LoadFile("$PWD\publish\windows\LenovoLegionToolkit.Lib.dll")

Write-Host "=== PHASE VERIFICATION IN COMPILED DLL ===" -ForegroundColor Cyan
Write-Host ""

# Phase 1: WMI Cache
Write-Host "PHASE 1: Critical Performance Fixes" -ForegroundColor Yellow
$wmiCache = $dll.GetType("LenovoLegionToolkit.Lib.System.Management.WMICache")
if ($wmiCache) {
    Write-Host "  ✓ WMICache class found" -ForegroundColor Green
    Write-Host "    Methods: $($wmiCache.GetMethods().Name -join ', ')" -ForegroundColor Gray
} else {
    Write-Host "  ✗ WMICache class NOT found" -ForegroundColor Red
}

# Phase 3: Feature Flags
$featureFlags = $dll.GetType("LenovoLegionToolkit.Lib.Utils.FeatureFlags")
if ($featureFlags) {
    Write-Host "  ✓ FeatureFlags class found" -ForegroundColor Green
    $props = $featureFlags.GetProperties() | Select-Object -ExpandProperty Name
    Write-Host "    Properties: $($props -join ', ')" -ForegroundColor Gray
} else {
    Write-Host "  ✗ FeatureFlags class NOT found" -ForegroundColor Red
}

# Phase 3: Performance Monitor
$perfMon = $dll.GetType("LenovoLegionToolkit.Lib.Utils.PerformanceMonitor")
if ($perfMon) {
    Write-Host "  ✓ PerformanceMonitor class found" -ForegroundColor Green
} else {
    Write-Host "  ✗ PerformanceMonitor class NOT found" -ForegroundColor Red
}

Write-Host ""
Write-Host "PHASE 4: Advanced Features" -ForegroundColor Yellow

# Phase 4: Reactive Sensors
$reactive = $dll.GetType("LenovoLegionToolkit.Lib.Controllers.Sensors.ReactiveSensorsController")
if ($reactive) {
    Write-Host "  ✓ ReactiveSensorsController found" -ForegroundColor Green
} else {
    Write-Host "  ✗ ReactiveSensorsController NOT found" -ForegroundColor Red
}

# Phase 4: Power Predictor
$predictor = $dll.GetType("LenovoLegionToolkit.Lib.AI.PowerUsagePredictor")
if ($predictor) {
    Write-Host "  ✓ PowerUsagePredictor found" -ForegroundColor Green
} else {
    Write-Host "  ✗ PowerUsagePredictor NOT found" -ForegroundColor Red
}

# Phase 4: Adaptive Fan Curve
$adaptive = $dll.GetType("LenovoLegionToolkit.Lib.Controllers.FanCurve.AdaptiveFanCurveController")
if ($adaptive) {
    Write-Host "  ✓ AdaptiveFanCurveController found" -ForegroundColor Green
} else {
    Write-Host "  ✗ AdaptiveFanCurveController NOT found" -ForegroundColor Red
}

# Phase 4: Object Pool
$pool = $dll.GetType("LenovoLegionToolkit.Lib.Utils.ObjectPool``1")
if ($pool) {
    Write-Host "  ✓ ObjectPool<T> found" -ForegroundColor Green
} else {
    Write-Host "  ✗ ObjectPool<T> NOT found" -ForegroundColor Red
}

Write-Host ""
Write-Host "DLL Timestamp: $(Get-Item 'publish\windows\LenovoLegionToolkit.Lib.dll').LastWriteTime"
