@echo off
REM Quick Installer Rebuild Script
REM Fixes the installation path issue

echo ==========================================
echo Rebuild Installer - Advanced Optimizations
echo ==========================================
echo.

REM Check for Inno Setup
set INNO_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe
if not exist "%INNO_PATH%" (
    echo ERROR: Inno Setup 6 not found at: %INNO_PATH%
    echo.
    echo Please install Inno Setup 6 from:
    echo https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

REM Verify publish directory
if not exist "publish\windows\Lenovo Legion Toolkit.exe" (
    echo ERROR: Main executable not found in publish\windows\
    echo.
    echo Please run build_gen9_enhanced.bat first to create the build
    echo.
    pause
    exit /b 1
)

echo Creating installer with corrected paths...
echo.

REM Create installer
"%INNO_PATH%" "make_installer.iss" /Q

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ==========================================
    echo INSTALLER CREATED SUCCESSFULLY
    echo ==========================================
    echo.
    echo Installer location: build_installer\LenovoLegionToolkitSetup.exe
    echo Version: 6.2.0
    echo.
    if exist "build_installer\LenovoLegionToolkitSetup.exe" (
        for %%F in ("build_installer\LenovoLegionToolkitSetup.exe") do (
            echo Installer size: %%~zF bytes
        )
    )
    echo.
    echo You can now run the installer:
    echo   build_installer\LenovoLegionToolkitSetup.exe
    echo.
) else (
    echo.
    echo ==========================================
    echo INSTALLER CREATION FAILED
    echo ==========================================
    echo.
    echo Error code: %ERRORLEVEL%
    echo Check make_installer.iss for issues
    echo.
)

pause
