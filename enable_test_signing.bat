@echo off
REM ================================================================
REM Enable Test Signing Mode for WinRing0 Kernel Driver
REM Version: v6.3.6 - Elite Kernel Developer Solution
REM ================================================================
REM
REM WHAT THIS DOES:
REM - Enables Windows Test Signing mode to allow unsigned kernel drivers
REM - Required for WinRing0x64.sys to load on Windows 11
REM - Resolves "Status=0x3" initialization error
REM
REM REQUIREMENTS:
REM - Must run as Administrator
REM - Requires system reboot after enabling
REM
REM ALTERNATIVE (if you don't want Test Signing):
REM - Disable Memory Integrity in Windows Security
REM - Disable Secure Boot in BIOS (not recommended)
REM - Use signed alternative like LibreHardwareMonitor (limited features)
REM ================================================================

echo ================================================================
echo WinRing0 Kernel Driver - Enable Test Signing Mode
echo Version: v6.3.6 - Elite Kernel Developer
echo ================================================================
echo.

REM Check for Administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script must be run as Administrator!
    echo.
    echo Right-click this file and select "Run as Administrator"
    echo.
    pause
    exit /b 1
)

echo Current Test Signing Status:
echo ================================================================
bcdedit /enum {current} | findstr /i "testsigning"
echo.

echo Current Memory Integrity Status:
echo ================================================================
powershell -Command "Get-CimInstance -Namespace root/Microsoft/Windows/DeviceGuard -ClassName Win32_DeviceGuard | Select-Object -ExpandProperty VirtualizationBasedSecurityStatus"
echo.

echo.
echo IMPORTANT INFORMATION:
echo ================================================================
echo WinRing0x64.sys is an UNSIGNED kernel driver (v1.3.1.19)
echo Windows 11 blocks unsigned drivers by default.
echo.
echo Status=0x3 means: Windows cannot load the driver file
echo.
echo SOLUTION OPTIONS:
echo.
echo [1] Enable Test Signing (allows unsigned drivers)
echo     - Pros: Easiest solution, keeps Secure Boot enabled
echo     - Cons: Watermark on desktop, slightly reduced security
echo     - Reversible: bcdedit /set testsigning off
echo.
echo [2] Disable Memory Integrity (HVCI)
echo     - Pros: No watermark, keeps Test Signing off
echo     - Cons: Reduces kernel security protection
echo     - Location: Windows Security ^> Device Security ^> Core Isolation
echo.
echo [3] Disable Secure Boot in BIOS (not recommended)
echo     - Pros: Allows all unsigned drivers
echo     - Cons: Significantly reduces system security
echo.
echo [4] Use alternative (no MSR access)
echo     - Application runs without WinRing0
echo     - MSR-based CPU optimizations disabled
echo     - Fan control still works (inpoutx64.dll)
echo.
echo ================================================================
echo.

choice /C 12N /M "Select option: [1] Enable Test Signing, [2] Disable Memory Integrity Instructions, [N] Cancel"
if errorlevel 3 goto :cancel
if errorlevel 2 goto :memory_integrity_info
if errorlevel 1 goto :enable_test_signing

:enable_test_signing
echo.
echo Enabling Test Signing mode...
echo ================================================================
bcdedit /set testsigning on

if %errorLevel% neq 0 (
    echo.
    echo ERROR: Failed to enable Test Signing!
    echo.
    echo Possible causes:
    echo - BitLocker is enabled (suspend BitLocker first)
    echo - Secure Boot Policy is enforced
    echo - Group Policy blocking
    echo.
    pause
    exit /b 1
)

echo.
echo SUCCESS: Test Signing mode ENABLED!
echo ================================================================
echo.
echo NEXT STEPS:
echo 1. REBOOT your computer (required)
echo 2. After reboot, you will see "Test Mode" watermark on desktop
echo 3. Launch Legion Toolkit with run_with_trace_logging.bat
echo 4. Check log for: "WinRing0 initialized: v1.3.1.19"
echo 5. MSR access should now be available
echo.
echo To disable Test Signing later:
echo   bcdedit /set testsigning off
echo   Reboot
echo.
choice /C YN /M "Reboot now?"
if errorlevel 2 goto :end
shutdown /r /t 10 /c "Rebooting to enable Test Signing for WinRing0 driver..."
echo.
echo System will reboot in 10 seconds...
echo To cancel: shutdown /a
echo.
goto :end

:memory_integrity_info
echo.
echo Disable Memory Integrity (HVCI) - Manual Steps:
echo ================================================================
echo.
echo 1. Press Win + I to open Settings
echo 2. Navigate to: Privacy ^& Security ^> Windows Security
echo 3. Click "Device security"
echo 4. Click "Core isolation details"
echo 5. Turn OFF "Memory integrity"
echo 6. Reboot your computer
echo 7. Launch Legion Toolkit with run_with_trace_logging.bat
echo.
echo After disabling Memory Integrity, WinRing0 should load successfully.
echo.
goto :end

:cancel
echo.
echo Operation cancelled.
echo.
echo Application will continue to work with limited functionality:
echo - ✅ Fan control (inpoutx64.dll)
echo - ✅ Power profiles
echo - ✅ Display settings
echo - ✅ RGB control
echo - ❌ MSR-based CPU optimizations (requires WinRing0)
echo - ❌ Hardware-level power limits
echo - ❌ C-State control
echo.

:end
pause
