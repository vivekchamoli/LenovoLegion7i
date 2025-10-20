@echo off
REM Restart Lenovo Legion Toolkit with Battery Drain Fix v6.22.4
REM This includes:
REM - CRITICAL FIX: Correct AC vs battery detection (discharge rate check)
REM - Enhanced BatteryAgent (25W/30W/40W thresholds)
REM - Trace logging toggle in Settings

echo ========================================
echo  BATTERY DRAIN FIX v6.22.4
echo ========================================
echo.
echo This build includes:
echo  [CRITICAL] Fixed AC detection bug
echo  [ENHANCED] Battery discharge warnings (25W/30W/40W)
echo  [FEATURE] Trace logging toggle in Settings
echo.
echo Current discharge rate: ~26.6W
echo Target after optimization: 8-15W
echo.
echo ========================================
echo.

REM Step 1: Close old application
echo [1/3] Closing running application...
taskkill /F /IM "Lenovo Legion Toolkit.exe" >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo    Application closed
) else (
    echo    No running application found
)
timeout /t 2 /nobreak >nul

REM Step 2: Start new build with trace logging
echo.
echo [2/3] Starting new build with trace logging...
start "" "C:\Users\Legion7\Desktop\All builds\LenovoLegion7iToolkit\publish\windows\Lenovo Legion Toolkit.exe" --trace
timeout /t 5 /nobreak >nul
echo    Application started

REM Step 3: Monitor logs
echo.
echo [3/3] Monitoring battery optimization logs...
echo.
echo Watch for these messages:
echo  - "IsOnBattery = true" (confirms fix working)
echo  - "ELEVATED battery discharge rate: 26.xW"
echo  - "ULTRA IDLE MODE ACTIVATED" (after 60s idle)
echo.
echo Log file location:
echo  %LOCALAPPDATA%\LenovoLegionToolkit\log\
echo.
echo Press Ctrl+C to stop monitoring
echo ========================================
echo.

REM Monitor latest log file
powershell -Command "$logDir = '$env:LOCALAPPDATA\LenovoLegionToolkit\log'; $latestLog = Get-ChildItem -Path $logDir -Filter 'log_*.txt' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($latestLog) { Write-Host \"Monitoring: $($latestLog.FullName)\"; Get-Content $latestLog.FullName -Wait -Tail 20 | Where-Object { $_ -match 'discharge|UltraIdle|IsOnBattery|ELEVATED|HIGH|CRITICAL' } } else { Write-Host 'No log file found' }"
