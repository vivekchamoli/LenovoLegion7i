@echo off
REM Quick Battery Drain Diagnostic Script
REM Shows current battery power consumption and status

echo ========================================
echo BATTERY DRAIN DIAGNOSTIC
echo ========================================
echo.

REM Get battery status
echo [1] Battery Status:
powershell -Command "Get-WmiObject -Class Win32_Battery | Select-Object EstimatedChargeRemaining, BatteryStatus, EstimatedRunTime | Format-List"
echo.

REM Get power consumption (requires admin)
echo [2] Current Power Consumption:
powershell -Command "$battery = Get-WmiObject -Class BatteryFullChargedCapacity -Namespace root/wmi; $status = Get-WmiObject -Class BatteryStatus -Namespace root/wmi; if ($status.DischargeRate -gt 0) { Write-Host \"Discharge Rate: $($status.DischargeRate / 1000) W\" -ForegroundColor Yellow } else { Write-Host 'On AC Power or Charging' -ForegroundColor Green }"
echo.

REM Check if Resource Orchestrator is running
echo [3] Resource Orchestrator Status:
cd "%LOCALAPPDATA%\LenovoLegionToolkit\log"
powershell -Command "Get-Content (Get-ChildItem -Filter 'log_*.txt' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName -Tail 100 | Select-String -Pattern 'ResourceOrchestrator|UltraIdle' | Select-Object -Last 10"
echo.

echo [4] Recent Battery Discharge Warnings:
powershell -Command "Get-Content (Get-ChildItem -Filter 'log_*.txt' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName | Select-String -Pattern 'discharge.*rate|ELEVATED|HIGH|CRITICAL' -CaseSensitive:$false | Select-Object -Last 10"
echo.

echo ========================================
echo.
pause
