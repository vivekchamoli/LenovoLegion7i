@echo off
REM Start Lenovo Legion Toolkit with trace logging enabled
REM This will show battery optimization in real-time

echo ========================================
echo Starting Lenovo Legion Toolkit
echo with TRACE LOGGING enabled
echo ========================================
echo.
echo This will help diagnose the 28-35W battery drain issue.
echo.
echo Watch for:
echo - "ELEVATED battery discharge rate" warnings (>25W)
echo - "HIGH battery discharge rate" warnings (>30W)
echo - UltraIdleOptimizer activation
echo.
echo Logs saved to: %LOCALAPPDATA%\LenovoLegionToolkit\log\
echo.
echo Press Ctrl+C to stop monitoring logs
echo ========================================
echo.

REM Start application with trace logging
start "" "C:\Users\Legion7\Desktop\All builds\LenovoLegion7iToolkit\publish\windows\Lenovo Legion Toolkit.exe" --trace

REM Wait for application to start
timeout /t 5 /nobreak > nul

REM Monitor logs in real-time
echo.
echo Monitoring logs (press Ctrl+C to stop)...
echo.

powershell -Command "Get-Content '%LOCALAPPDATA%\LenovoLegionToolkit\log\log_*.txt' -Tail 50 -Wait | Select-String -Pattern 'discharge|UltraIdle|BatteryAgent|Orchestrator' -CaseSensitive:$false"
