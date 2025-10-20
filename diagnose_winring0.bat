@echo off
setlocal enabledelayedexpansion
:: ==============================================================================
:: WinRing0 Driver Diagnostic and Installation Script
:: Diagnoses WinRing0 driver issues (Status=0x3 ERROR_PATH_NOT_FOUND)
:: ==============================================================================

echo.
echo ======================================================================
echo  WinRing0 Driver Diagnostic Tool
echo ======================================================================
echo.

:: Check if running as Administrator
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] This script requires Administrator privileges.
    echo         Right-click and select "Run as Administrator"
    pause
    exit /b 1
)

echo [1/6] Checking WinRing0x64.dll in application directory...
if exist "%LOCALAPPDATA%\Programs\LenovoLegionToolkit\WinRing0x64.dll" (
    echo     [OK] WinRing0x64.dll found
    dir "%LOCALAPPDATA%\Programs\LenovoLegionToolkit\WinRing0x64.dll" | findstr /i "WinRing0x64.dll"
) else (
    echo     [FAIL] WinRing0x64.dll NOT FOUND
    echo     Expected location: %LOCALAPPDATA%\Programs\LenovoLegionToolkit\WinRing0x64.dll
)

echo.
echo [2/6] Checking WinRing0x64.sys driver file...
set DRIVER_SYS_FOUND=0
if exist "%LOCALAPPDATA%\Programs\LenovoLegionToolkit\WinRing0x64.sys" (
    echo     [OK] WinRing0x64.sys found in application directory
    set DRIVER_SYS_FOUND=1
) else if exist "%~dp0LenovoLegionToolkit.Lib\Native\WinRing0x64.sys" (
    echo     [OK] WinRing0x64.sys found in Native folder
    set DRIVER_SYS_FOUND=1
) else (
    echo     [FAIL] WinRing0x64.sys NOT FOUND
    echo     This is CRITICAL - driver file is missing!
)

echo.
echo [3/6] Checking if WinRing0 driver is installed in Windows...
sc query WinRing0_1_2_0 >nul 2>&1
if %errorlevel% equ 0 (
    echo     [OK] WinRing0 service is registered
    sc query WinRing0_1_2_0
) else (
    echo     [FAIL] WinRing0 service NOT registered (Status=0x3 ERROR_PATH_NOT_FOUND)
    echo     This is the root cause of your initialization failure!
)

echo.
echo [4/6] Checking Test Signing mode (required for unsigned drivers)...
bcdedit /enum {current} | findstr /i "testsigning"
if %errorlevel% equ 0 (
    echo     [OK] Test Signing is enabled
) else (
    echo     [WARNING] Test Signing is DISABLED
    echo     WinRing0 driver may fail to load without Test Signing OR Secure Boot disabled
)

echo.
echo [5/6] Checking Secure Boot status...
powershell -Command "Confirm-SecureBootUEFI" 2>nul
if %errorlevel% equ 0 (
    echo     [WARNING] Secure Boot is ENABLED
    echo     WinRing0 unsigned driver will NOT load with Secure Boot enabled
    echo     You must EITHER:
    echo       1. Disable Secure Boot in BIOS/UEFI, OR
    echo       2. Enable Test Signing: bcdedit /set testsigning on
) else (
    echo     [OK] Secure Boot is disabled (driver can load)
)

echo.
echo [6/6] Checking inpoutx64.dll (EC access fallback)...
if exist "%LOCALAPPDATA%\Programs\LenovoLegionToolkit\inpoutx64.dll" (
    echo     [OK] inpoutx64.dll found (EC access will work)
) else (
    echo     [WARNING] inpoutx64.dll NOT FOUND
    echo     EC sensor readings may fail
)

echo.
echo ======================================================================
echo  Diagnostic Summary
echo ======================================================================
echo.

if %DRIVER_SYS_FOUND% equ 0 (
    echo [CRITICAL] WinRing0x64.sys is MISSING - driver cannot be installed!
    echo.
    echo SOLUTION:
    echo   1. Copy WinRing0x64.sys from LenovoLegionToolkit.Lib\Native\ to:
    echo      %LOCALAPPDATA%\Programs\LenovoLegionToolkit\
    echo   2. Rebuild the application to ensure driver files are copied
    echo.
) else (
    sc query WinRing0_1_2_0 >nul 2>&1
    if %errorlevel% neq 0 (
        echo [CRITICAL] WinRing0 driver service NOT installed
        echo.
        echo SOLUTION:
        echo   The application should auto-install the driver on first run.
        echo   If auto-install fails, you must:
        echo     1. Disable Secure Boot in BIOS/UEFI, OR
        echo     2. Enable Test Signing: bcdedit /set testsigning on ^(requires reboot^)
        echo     3. Reboot Windows
        echo     4. Run Lenovo Legion Toolkit as Administrator
        echo.
    ) else (
        echo [OK] WinRing0 driver is installed and should work
        echo.
        echo If you still see "Status=0x3" errors, try:
        echo   1. Stop any running Lenovo Legion Toolkit processes
        echo   2. sc stop WinRing0_1_2_0
        echo   3. sc delete WinRing0_1_2_0
        echo   4. Reboot
        echo   5. Run Lenovo Legion Toolkit as Administrator
        echo.
    )
)

echo.
echo Press any key to exit...
pause >nul
