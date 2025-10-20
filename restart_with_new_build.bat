@echo off
echo ============================================================
echo Lenovo Legion Toolkit - Restart with New Build
echo ============================================================
echo.

echo [Step 1/4] Stopping all Legion Toolkit processes...
taskkill /F /IM "Lenovo Legion Toolkit.exe" 2>nul
if %ERRORLEVEL% == 0 (
    echo SUCCESS: Application stopped
) else (
    echo INFO: No running processes found
)
echo.

echo [Step 2/4] Waiting 3 seconds for cleanup...
timeout /t 3 /nobreak >nul
echo.

echo [Step 3/4] Setting trace logging environment variable...
set LENOVO_LEGION_TOOLKIT_LOG_LEVEL=Trace
echo SUCCESS: Trace logging enabled
echo.

echo [Step 4/4] Starting NEW build from Release directory...
echo Path: LenovoLegionToolkit.WPF\bin\Release\net8.0-windows\win-x64\Lenovo Legion Toolkit.exe
start "" "LenovoLegionToolkit.WPF\bin\Release\net8.0-windows\win-x64\Lenovo Legion Toolkit.exe"
echo.

echo ============================================================
echo Application started! Check logs in:
echo %LOCALAPPDATA%\LenovoLegionToolkit\log\
echo.
echo Expected to see in log:
echo - [FeatureChangeInterceptor] Initialized with full cooling period support
echo - After changing refresh rate:
echo   [FeatureChangeInterceptor] User changed DISPLAY_REFRESH_RATE to 165Hz...
echo   [DisplayAgent] DISPLAY_REFRESH_RATE cooling period active...
echo ============================================================
echo.
pause
