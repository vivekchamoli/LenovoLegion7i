@echo off
REM Legion Toolkit Windows Build Script v6.0.0
REM Zero-Error Build Script for Windows (WPF Application)
REM Complete error handling and validation system

setlocal enabledelayedexpansion

echo ==========================================
echo Legion Toolkit Windows Build System
echo Version: 6.0.0 - Production Ready
echo Platform: Windows (WPF)
echo ==========================================
echo.

REM Initialize build variables
set BUILD_SUCCESS=0
set BUILD_DIR=%CD%
set PUBLISH_DIR=%BUILD_DIR%\publish\windows
set BUILD_LOG=%BUILD_DIR%\build.log

REM Clear previous log
if exist "%BUILD_LOG%" del "%BUILD_LOG%"

echo [%TIME%] Starting Windows build process... >> "%BUILD_LOG%"

REM ============================================
REM Phase 0: Pre-build validation
REM ============================================
echo Phase 0: Pre-build Validation
echo ====================================

REM Check .NET SDK
echo Checking .NET SDK...
dotnet --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: .NET SDK not found. Please install .NET 8.0 SDK.
    echo [%TIME%] ERROR: .NET SDK not found >> "%BUILD_LOG%"
    goto :error_exit
)

for /f "tokens=*" %%i in ('dotnet --version') do set DOTNET_VERSION=%%i
echo [OK] .NET SDK Version: %DOTNET_VERSION%
echo [%TIME%] .NET SDK Version: %DOTNET_VERSION% >> "%BUILD_LOG%"

REM Validate .NET 8 requirement
echo %DOTNET_VERSION% | findstr /R "^8\." >nul
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: .NET 8.0 recommended, found %DOTNET_VERSION%
    echo [%TIME%] WARNING: Non-optimal .NET version: %DOTNET_VERSION% >> "%BUILD_LOG%"
)

REM Check solution file
if not exist "LenovoLegionToolkit.sln" (
    echo ERROR: Solution file LenovoLegionToolkit.sln not found
    echo [%TIME%] ERROR: Solution file not found >> "%BUILD_LOG%"
    goto :error_exit
)
echo [OK] Solution file found

REM Check main project file
if not exist "LenovoLegionToolkit.WPF\LenovoLegionToolkit.WPF.csproj" (
    echo ERROR: Main project file not found
    echo [%TIME%] ERROR: Main project file not found >> "%BUILD_LOG%"
    goto :error_exit
)
echo [OK] Main project file found

REM ============================================
REM Phase 1: Clean and restore
REM ============================================
echo.
echo Phase 1: Clean and Restore
echo ====================================

REM Clean previous builds with error handling
echo Cleaning previous builds...
if exist "bin" (
    rmdir /s /q "bin" 2>nul
    if exist "bin" (
        echo WARNING: Could not fully clean bin directory
        echo [%TIME%] WARNING: bin directory cleanup incomplete >> "%BUILD_LOG%"
    )
)

if exist "obj" (
    rmdir /s /q "obj" 2>nul
    if exist "obj" (
        echo WARNING: Could not fully clean obj directory
        echo [%TIME%] WARNING: obj directory cleanup incomplete >> "%BUILD_LOG%"
    )
)

if exist "%PUBLISH_DIR%" (
    rmdir /s /q "%PUBLISH_DIR%" 2>nul
    if exist "%PUBLISH_DIR%" (
        echo WARNING: Could not clean publish directory
        echo [%TIME%] WARNING: publish directory cleanup incomplete >> "%BUILD_LOG%"
    )
)

echo [OK] Build directories cleaned

REM Restore NuGet packages with enhanced error handling
echo Restoring NuGet packages...
echo [%TIME%] Starting package restore >> "%BUILD_LOG%"

dotnet restore LenovoLegionToolkit.sln --verbosity minimal 2>>"%BUILD_LOG%"
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to restore NuGet packages
    echo [%TIME%] ERROR: Package restore failed with code %ERRORLEVEL% >> "%BUILD_LOG%"
    goto :error_exit
)

echo [OK] NuGet packages restored successfully

REM ============================================
REM Phase 2: Build Windows application
REM ============================================
echo.
echo Phase 2: Build Windows Application (WPF)
echo ====================================

echo Building Legion Toolkit for Windows...
echo [%TIME%] Starting Windows build >> "%BUILD_LOG%"

REM Create publish directory
mkdir "%PUBLISH_DIR%" 2>nul

REM Build with comprehensive error checking
dotnet publish "LenovoLegionToolkit.WPF\LenovoLegionToolkit.WPF.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained false ^
    -o "%PUBLISH_DIR%" ^
    --verbosity minimal ^
    2>>"%BUILD_LOG%"

if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Windows build failed with exit code %ERRORLEVEL%
    echo [%TIME%] ERROR: Windows build failed with code %ERRORLEVEL% >> "%BUILD_LOG%"
    goto :error_exit
)

REM Validate build output
if not exist "%PUBLISH_DIR%\Lenovo Legion Toolkit.exe" (
    echo ERROR: Main executable not found after build
    echo [%TIME%] ERROR: Main executable missing after build >> "%BUILD_LOG%"
    goto :error_exit
)

echo [OK] Windows application built successfully

REM Check executable properties
for %%F in ("%PUBLISH_DIR%\Lenovo Legion Toolkit.exe") do (
    echo   - Executable size: %%~zF bytes
    echo [%TIME%] Executable size: %%~zF bytes >> "%BUILD_LOG%"
)

REM ============================================
REM Phase 3: Create Windows installer
REM ============================================
echo.
echo Phase 3: Create Windows Installer
echo ====================================

REM Check for Inno Setup
set INNO_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe
if exist "%INNO_PATH%" (
    echo Creating Windows installer...
    echo [%TIME%] Starting installer creation >> "%BUILD_LOG%"

    REM Check installer script
    if not exist "make_installer.iss" (
        echo ERROR: Installer script make_installer.iss not found
        echo [%TIME%] ERROR: Installer script missing >> "%BUILD_LOG%"
        goto :error_exit
    )

    REM Create installer with error checking
    "%INNO_PATH%" "make_installer.iss" /Q 2>>"%BUILD_LOG%"
    if %ERRORLEVEL% EQU 0 (
        echo [OK] Windows installer created successfully

        REM Validate installer output
        if exist "build_installer\LenovoLegionToolkitSetup.exe" (
            for %%F in ("build_installer\LenovoLegionToolkitSetup.exe") do (
                echo   - Installer size: %%~zF bytes
                echo [%TIME%] Installer size: %%~zF bytes >> "%BUILD_LOG%"
            )
        ) else (
            echo WARNING: Installer file not found at expected location
            echo [%TIME%] WARNING: Installer file missing >> "%BUILD_LOG%"
        )
    ) else (
        echo WARNING: Installer creation failed with code %ERRORLEVEL%
        echo [%TIME%] WARNING: Installer creation failed >> "%BUILD_LOG%"
    )
) else (
    echo WARNING: Inno Setup not found, skipping installer creation
    echo   Install Inno Setup 6 from: https://jrsoftware.org/isdl.php
    echo [%TIME%] WARNING: Inno Setup not available >> "%BUILD_LOG%"
)

REM ============================================
REM Phase 4: Create documentation
REM ============================================
echo.
echo Phase 4: Create Build Documentation
echo ====================================

echo Creating build documentation...

REM Create comprehensive build report
(
echo # Legion Toolkit v6.0.0 - Windows Build Report
echo.
echo **Build Date**: %DATE% %TIME%
echo **Platform**: Windows ^(WPF^)
echo **Build Environment**: Windows
echo **Repository**: https://github.com/vivekchamoli/LenovoLegion7i
echo.
echo ## Build Results
echo.
echo ### Windows Components
echo - [OK] .NET 8.0 WPF Application: %PUBLISH_DIR%\Lenovo Legion Toolkit.exe
if exist "build_installer\LenovoLegionToolkitSetup.exe" (
    echo - [OK] Windows Installer: build_installer\LenovoLegionToolkitSetup.exe
) else (
    echo - [SKIP] Windows Installer: Not created ^(Inno Setup required^)
)
echo.
echo ## Installation Instructions
echo.
echo ### Windows Installation
echo 1. Run build_installer\LenovoLegionToolkitSetup.exe as Administrator
echo 2. Follow installation wizard
echo 3. Launch from Start Menu: "Lenovo Legion Toolkit"
echo.
echo ### Manual Installation
echo 1. Ensure .NET 8.0 Runtime is installed
echo 2. Copy %PUBLISH_DIR% folder to desired location
echo 3. Run "Lenovo Legion Toolkit.exe"
echo.
echo ## System Requirements
echo - Windows 10/11 ^(64-bit^)
echo - .NET 8.0 Runtime or later
echo - Lenovo Legion laptop ^(for full hardware functionality^)
echo.
echo ## Version Information
echo - Application Version: 6.0.0
echo - .NET Version: %DOTNET_VERSION%
echo - Build Configuration: Release
echo - Target Architecture: x64
echo - Platform: Windows WPF
echo.
echo ## Project Information
echo - Repository: https://github.com/vivekchamoli/LenovoLegion7i
echo - Platform: Windows-only
echo - License: MIT
echo - Author: Vivek Chamoli
echo.
echo Built with Legion Toolkit Windows Build System
) > "%PUBLISH_DIR%\BUILD_INFO.md"

echo [OK] Build documentation created

REM ============================================
REM Phase 5: Final validation
REM ============================================
echo.
echo Phase 5: Final Validation
echo ====================================

echo Performing final validation...

set VALIDATION_ERRORS=0

REM Validate Windows executable
if not exist "%PUBLISH_DIR%\Lenovo Legion Toolkit.exe" (
    echo ERROR: Main Windows executable missing
    set /a VALIDATION_ERRORS+=1
)

REM Validate core libraries
if not exist "%PUBLISH_DIR%\LenovoLegionToolkit.Lib.dll" (
    echo ERROR: Core library missing
    set /a VALIDATION_ERRORS+=1
)

REM Validate documentation
if not exist "%PUBLISH_DIR%\BUILD_INFO.md" (
    echo ERROR: Build documentation missing
    set /a VALIDATION_ERRORS+=1
)

if %VALIDATION_ERRORS% GTR 0 (
    echo ERROR: Validation failed with %VALIDATION_ERRORS% errors
    echo [%TIME%] ERROR: Final validation failed >> "%BUILD_LOG%"
    goto :error_exit
)

echo [OK] All validations passed

REM ============================================
REM Build complete
REM ============================================
echo.
echo ==========================================
echo BUILD COMPLETED SUCCESSFULLY
echo ==========================================
echo.
echo Build Summary:
echo   - Windows Application: READY
if exist "build_installer\LenovoLegionToolkitSetup.exe" (
    echo   - Windows Installer: CREATED
) else (
    echo   - Windows Installer: SKIPPED (Install Inno Setup 6)
)
echo   - Documentation: CREATED
echo.
echo Output Locations:
echo   - Application: %PUBLISH_DIR%\
if exist "build_installer\LenovoLegionToolkitSetup.exe" (
    echo   - Installer: build_installer\LenovoLegionToolkitSetup.exe
)
echo   - Documentation: %PUBLISH_DIR%\BUILD_INFO.md
echo   - Build Log: %BUILD_LOG%
echo.
echo Legion Toolkit v6.0.0 - Windows Edition
echo Platform: Windows WPF Application
echo Repository: https://github.com/vivekchamoli/LenovoLegion7i
echo.

set BUILD_SUCCESS=1
echo [%TIME%] Build completed successfully >> "%BUILD_LOG%"
goto :exit

:error_exit
echo.
echo ==========================================
echo BUILD FAILED
echo ==========================================
echo.
echo Check the build log for details: %BUILD_LOG%
echo.
set BUILD_SUCCESS=0
pause
exit /b 1

:exit
if %BUILD_SUCCESS% EQU 1 (
    echo Build completed successfully!
    echo Check %PUBLISH_DIR%\BUILD_INFO.md for detailed information.
) else (
    echo Build encountered issues. Check %BUILD_LOG% for details.
)
echo.
pause
exit /b 0
